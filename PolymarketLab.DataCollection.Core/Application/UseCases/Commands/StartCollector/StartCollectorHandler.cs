using CSharpFunctionalExtensions;
using MediatR;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
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
    ICollectorRuntime runtime,
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
        var activeSession = await sessionRepository.GetActiveByMarketIdAsync(
            marketId,
            cancellationToken);
        if (activeSession is not null)
            return Existing(activeSession);

        var marketResult = await marketSource.GetByIdAsync(marketId, cancellationToken);
        if (marketResult.IsFailure)
            return Failure(marketResult.Error);

        var market = marketResult.Value;
        if (market is null)
            return Failure(StartCollectorErrors.MarketNotFound(command.MarketId));

        if (!market.Active
            || market.Closed
            || !market.AcceptingOrders
            || !market.OrderBookEnabled)
        {
            return Failure(StartCollectorErrors.MarketUnavailable(command.MarketId));
        }

        var tokenError = ValidateTokens(market);
        if (tokenError is not null)
            return Failure(tokenError);

        var sessionIdResult =
            CollectorSessionId.Create(Guid.NewGuid());
        if (sessionIdResult.IsFailure)
            return Failure(sessionIdResult.Error);

        var sessionResult = CollectorSessionAggregate.Create(
            sessionIdResult.Value,
            marketId,
            timeProvider.GetUtcNow());
        if (sessionResult.IsFailure)
            return Failure(sessionResult.Error);

        var session = sessionResult.Value;
        var insertResult =
            await sessionRepository.TryAddAsync(session, cancellationToken);
        if (insertResult.IsFailure)
            return Failure(insertResult.Error);

        if (insertResult.Value ==
            CollectorSessionInsertStatus.ActiveSessionConflict)
        {
            activeSession = await sessionRepository.GetActiveByMarketIdAsync(
                marketId,
                cancellationToken);

            return activeSession is not null
                ? Existing(activeSession)
                : Failure(StartCollectorErrors.RaceUnresolved);
        }

        if (insertResult.Value != CollectorSessionInsertStatus.Inserted)
            return Failure(StartCollectorErrors.RaceUnresolved);

        UnitResult<Error> runtimeResult;
        try
        {
            runtimeResult = await runtime.StartAsync(
                new CollectorRuntimeStartRequest(session.Id, market),
                cancellationToken);
        }
        catch (OperationCanceledException) when
               (cancellationToken.IsCancellationRequested)
        {
            var compensationResult = await MarkFailedAsync(
                session,
                CollectorSessionStatus.Starting,
                CollectorStopReason.StartupFailure,
                StartCollectorErrors.RuntimeStartCancelled);

            if (compensationResult.IsFailure)
                return Failure(compensationResult.Error);

            throw;
        }

        if (runtimeResult.IsFailure)
        {
            var compensationResult = await MarkFailedAsync(
                session,
                CollectorSessionStatus.Starting,
                CollectorStopReason.StartupFailure,
                runtimeResult.Error);

            return compensationResult.IsFailure
                ? Failure(runtimeResult.Error, compensationResult.Error)
                : Failure(runtimeResult.Error);
        }

        var markRunningResult = session.MarkRunning(timeProvider.GetUtcNow());
        if (markRunningResult.IsFailure)
            return await CompensateStartedRuntimeAsync(session, markRunningResult.Error);

        var updateResult = await sessionRepository.TryUpdateAsync(
            session,
            CollectorSessionStatus.Starting,
            CancellationToken.None);
        if (updateResult.IsFailure)
            return await CompensateStartedRuntimeAsync(session, updateResult.Error);

        if (updateResult.Value == CollectorSessionUpdateStatus.ConcurrencyConflict)
            return await HandleRunningConflictAsync(session);

        return Response(session);
    }

    private async Task<Result<StartCollectorResponse, ErrorList>> CompensateStartedRuntimeAsync(
        CollectorSessionAggregate session,
        Error cause)
    {
        var stopResult = await runtime.StopAsync(session.Id, CancellationToken.None);
        if (stopResult.IsFailure)
            return Failure(cause, stopResult.Error);

        var failResult = await MarkFailedAsync(
            session,
            CollectorSessionStatus.Starting,
            CollectorStopReason.PersistenceFailure,
            cause);

        return failResult.IsFailure
            ? Failure(cause, failResult.Error)
            : Failure(cause);
    }

    private async Task<UnitResult<Error>> MarkFailedAsync(
        CollectorSessionAggregate session,
        CollectorSessionStatus expectedStatus,
        CollectorStopReason reason,
        Error error)
    {
        var failResult = session.Fail(
            timeProvider.GetUtcNow(),
            reason,
            error.Code,
            error.Message);
        if (failResult.IsFailure)
            return failResult;

        var updateResult = await sessionRepository.TryUpdateAsync(
            session,
            expectedStatus,
            CancellationToken.None);
        if (updateResult.IsFailure)
            return UnitResult.Failure(updateResult.Error);

        return updateResult.Value == CollectorSessionUpdateStatus.Updated
            ? UnitResult.Success<Error>()
            : UnitResult.Failure(StartCollectorErrors.StateTransitionConflict);
    }

    private async Task<Result<StartCollectorResponse, ErrorList>> HandleRunningConflictAsync(
        CollectorSessionAggregate session)
    {
        var stopResult = await runtime.StopAsync(session.Id, CancellationToken.None);
        var persistedSession = await sessionRepository.GetByIdAsync(
            session.Id,
            CancellationToken.None);

        var cause = persistedSession is
        {
            Status: CollectorSessionStatus.Failed,
            FailureCode: not null,
            FailureMessage: not null
        }
            ? new Error(
                persistedSession.FailureCode,
                persistedSession.FailureMessage,
                ErrorType.Failure)
            : StartCollectorErrors.StateTransitionConflict;

        return stopResult.IsFailure
            ? Failure(cause, stopResult.Error)
            : Failure(cause);
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

    private static Result<StartCollectorResponse, ErrorList> Existing(
        CollectorSessionAggregate session)
    {
        return Response(session);
    }

    private static StartCollectorResponse Response(CollectorSessionAggregate session)
    {
        return new StartCollectorResponse(
            session.Id.Value,
            session.MarketId.Value,
            session.Status.ToString());
    }

    private static Result<StartCollectorResponse, ErrorList> Failure(params Error[] errors)
    {
        return Result.Failure<StartCollectorResponse, ErrorList>(errors.ToList());
    }
}
