using CSharpFunctionalExtensions;
using MediatR;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorScheduling;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using static PolymarketLab.SharedKernel.Errors.Error;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StartCollector;

public sealed class StartCollectorHandler(
    IMarketCollectionSource marketSource,
    ICollectorSessionRepository sessionRepository,
    IProjectionVersionProvider projectionVersionProvider,
    ICollectorScheduler scheduler,
    TimeProvider timeProvider)
    : IRequestHandler<StartCollectorCommand, Result<StartCollectorResponse, ErrorList>>
{
    public async Task<Result<StartCollectorResponse, ErrorList>> Handle(
        StartCollectorCommand command,
        CancellationToken cancellationToken)
    {
        var marketIdResult = MarketId.Create(command.MarketId);
        if (marketIdResult.IsFailure)
            return Failure(marketIdResult.Error);

        var marketId = marketIdResult.Value;
        var exclusiveSession = await sessionRepository.GetExclusiveAsync(cancellationToken);
        if (exclusiveSession is not null)
            return ResolveExclusiveSession(exclusiveSession, marketId);

        var window = await marketSource.GetWindowAsync(marketId, cancellationToken);
        if (window is null)
            return Failure(StartCollectorErrors.MarketNotFound(command.MarketId));
        if (window.EventStartsAt <= timeProvider.GetUtcNow())
            return Failure(StartCollectorErrors.MarketAlreadyOpen(command.MarketId));

        var marketResult = await marketSource.GetByIdAsync(marketId, cancellationToken);
        if (marketResult.IsFailure)
            return Failure(marketResult.Error);

        var market = marketResult.Value;
        if (market is null)
            return Failure(StartCollectorErrors.MarketNotFound(command.MarketId));
        var verifiedAt = timeProvider.GetUtcNow();
        if (window.EventStartsAt <= verifiedAt || market.EventStartsAt <= verifiedAt)
            return Failure(StartCollectorErrors.MarketAlreadyOpen(command.MarketId));

        var tokenError = ValidateTokens(market);
        if (tokenError is not null)
            return Failure(tokenError);

        var sessionIdResult = CollectorSessionId.Create(Guid.NewGuid());
        if (sessionIdResult.IsFailure)
            return Failure(sessionIdResult.Error);

        var tokenDefinitions = market.Tokens
            .Select(token => new CollectorSessionTokenDefinition(
                token.TokenId,
                token.Outcome,
                token.OutcomeIndex))
            .ToArray();
        var sessionResult = CollectorSessionAggregate.Create(
            sessionIdResult.Value,
            marketId,
            market.ExternalEventId,
            market.EventSlug,
            market.ExternalMarketId,
            market.MarketSlug,
            market.ConditionId,
            market.EventStartsAt,
            market.EventEndsAt,
            projectionVersionProvider.ProjectionVersion,
            tokenDefinitions,
            verifiedAt);
        if (sessionResult.IsFailure)
            return Failure(sessionResult.Error);

        var session = sessionResult.Value;
        var insertResult = await sessionRepository.TryAddAsync(session, cancellationToken);
        if (insertResult.IsFailure)
            return Failure(insertResult.Error);

        if (insertResult.Value == CollectorSessionInsertStatus.Inserted)
        {
            var schedulingResult = await scheduler.PrepareAsync(
                session,
                market,
                cancellationToken);
            return schedulingResult.IsFailure
                ? Failure(schedulingResult.Error)
                : Response(schedulingResult.Value);
        }
        if (insertResult.Value != CollectorSessionInsertStatus.ExclusiveSessionConflict)
            return Failure(StartCollectorErrors.RaceUnresolved);

        exclusiveSession = await sessionRepository.GetExclusiveAsync(cancellationToken);
        return exclusiveSession is null
            ? Failure(StartCollectorErrors.RaceUnresolved)
            : ResolveExclusiveSession(exclusiveSession, marketId);
    }

    private static Error? ValidateTokens(CollectionMarket market)
    {
        if (market.Tokens is null || market.Tokens.Count < 2)
            return StartCollectorErrors.TokensRequired(market.Tokens?.Count ?? 0);

        var tokenWithoutOutcome = market.Tokens.FirstOrDefault(
            token => string.IsNullOrWhiteSpace(token.Outcome));
        if (tokenWithoutOutcome is not null)
            return StartCollectorErrors.TokenOutcomeRequired(tokenWithoutOutcome.OutcomeIndex);

        var duplicateTokenId = market.Tokens
            .GroupBy(token => token.TokenId)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateTokenId is not null)
            return StartCollectorErrors.DuplicateTokenId(duplicateTokenId.Key.Value);

        var duplicateOutcomeIndex = market.Tokens
            .GroupBy(token => token.OutcomeIndex)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateOutcomeIndex is not null)
            return StartCollectorErrors.DuplicateOutcomeIndex(duplicateOutcomeIndex.Key);

        return null;
    }

    private static Result<StartCollectorResponse, ErrorList> ResolveExclusiveSession(
        CollectorSessionAggregate session,
        MarketId requestedMarketId) =>
        session.MarketId == requestedMarketId
            ? Response(session)
            : Failure(StartCollectorErrors.GlobalSessionConflict);

    private static StartCollectorResponse Response(CollectorSessionAggregate session) =>
        new(session.Id.Value, session.MarketId.Value, session.Status.ToString());

    private static Result<StartCollectorResponse, ErrorList> Failure(params Error[] errors) =>
        Result.Failure<StartCollectorResponse, ErrorList>(errors.ToList());
}
