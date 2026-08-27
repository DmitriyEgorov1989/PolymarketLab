using CSharpFunctionalExtensions;
using MediatR;
using PolymarketLab.Markets.Core.Application.Errors;
using PolymarketLab.Markets.Core.Application.Extensions;
using PolymarketLab.Markets.Core.Application.Integration;
using PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects;
using PolymarketLab.Markets.Core.Ports;
using PolymarketLab.Markets.Core.Ports.Dto;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using static PolymarketLab.SharedKernel.Errors.Error;
using MarketAggregate = PolymarketLab.Markets.Core.Domain.Models.Market.MarketAggregate.Market;

namespace PolymarketLab.Markets.Core.Application.UseCases.Commands;

public sealed class RegisterMarketHandler(
    IExternalMarketGateway externalMarketGateway,
    IMarketRepository marketRepository,
    TimeProvider timeProvider)
    : IRequestHandler<RegisterMarketCommand, Result<RegisterMarketResponse, ErrorList>>
{
    public async Task<Result<RegisterMarketResponse, ErrorList>> Handle(
        RegisterMarketCommand request,
        CancellationToken cancellationToken)
    {
        var eventSlugResult = request.MarketUri.ParsePolymarketEventSlug();
        if (eventSlugResult.IsFailure)
            return Failure(eventSlugResult.Error);

        var requestedEventSlug = eventSlugResult.Value;
        var externalEventResult = await externalMarketGateway.GetByEventSlugAsync(
            requestedEventSlug,
            cancellationToken);
        if (externalEventResult.IsFailure)
            return Failure(externalEventResult.Error);

        var externalEvent = externalEventResult.Value;
        var eventSlugResultFromSource = EventSlug.Create(externalEvent.Slug);
        if (eventSlugResultFromSource.IsFailure)
            return Failure(eventSlugResultFromSource.Error);

        var eventSlug = eventSlugResultFromSource.Value;
        if (!requestedEventSlug.Equals(eventSlug))
        {
            return Failure(MarketRegistrationErrors.EventSlugMismatch(
                requestedEventSlug.Value,
                eventSlug.Value));
        }

        var externalEventIdResult = ExternalEventId.Create(externalEvent.ExternalEventId);
        if (externalEventIdResult.IsFailure)
            return Failure(externalEventIdResult.Error);

        var candidateResult = CreateCandidate(externalEvent, externalEventIdResult.Value, eventSlug);
        if (candidateResult.IsFailure)
            return Failure(candidateResult.Error);

        var candidate = candidateResult.Value;
        var identity = await ResolveIdentityAsync(candidate, cancellationToken);
        if (identity.Conflict)
            return Failure(MarketRegistrationErrors.IdentityConflict);

        if (identity.Market is not null)
            return await RefreshExistingAsync(identity.Market, candidate, cancellationToken);

        if (!externalEvent.Market.OrderBookEnabled)
            return Failure(MarketRegistrationErrors.OrderBookDisabled);

        if (MarketAvailability.IsTerminal(externalEvent.Market))
            return Failure(MarketRegistrationErrors.Unavailable);

        var insertResult = await marketRepository.TryAddAsync(candidate, cancellationToken);
        if (insertResult.IsFailure)
            return Failure(insertResult.Error);

        if (insertResult.Value == MarketInsertStatus.Inserted)
            return new RegisterMarketResponse(candidate.Id.Value, true);

        if (insertResult.Value != MarketInsertStatus.UniqueConflict)
            return Failure(MarketRegistrationErrors.RaceUnresolved);

        identity = await ResolveIdentityAsync(candidate, cancellationToken);
        if (identity.Conflict)
            return Failure(MarketRegistrationErrors.IdentityConflict);

        return identity.Market is not null
            ? await RefreshExistingAsync(identity.Market, candidate, cancellationToken)
            : Failure(MarketRegistrationErrors.RaceUnresolved);
    }

    private Result<MarketAggregate, Error> CreateCandidate(
        ExternalEvent externalEvent,
        ExternalEventId externalEventId,
        EventSlug eventSlug)
    {
        var externalMarket = externalEvent.Market;
        var externalMarketIdResult = ExternalMarketId.Create(externalMarket.ExternalMarketId);
        if (externalMarketIdResult.IsFailure)
            return externalMarketIdResult.Error;

        var marketSlugResult = MarketSlug.Create(externalMarket.Slug);
        if (marketSlugResult.IsFailure)
            return marketSlugResult.Error;

        var conditionIdResult = ConditionId.Create(externalMarket.ConditionId);
        if (conditionIdResult.IsFailure)
            return conditionIdResult.Error;

        if (externalMarket.Tokens is null || externalMarket.Tokens.Count == 0)
            return MarketRegistrationErrors.TokensRequired;

        if (externalMarket.EventStartsAt is null)
            return GeneralErrors.ValueIsRequired(nameof(externalMarket.EventStartsAt));

        if (externalMarket.EventEndsAt is null)
            return GeneralErrors.ValueIsRequired(nameof(externalMarket.EventEndsAt));

        var marketIdResult = MarketId.Create(Guid.NewGuid());
        if (marketIdResult.IsFailure)
            return marketIdResult.Error;

        var now = timeProvider.GetUtcNow();
        var marketResult = MarketAggregate.Create(
            marketIdResult.Value,
            externalEventId,
            eventSlug,
            externalMarketIdResult.Value,
            marketSlugResult.Value,
            conditionIdResult.Value,
            externalMarket.Question,
            now,
            externalMarket.ExternalCreatedAt,
            externalMarket.OrdersOpenedAt,
            externalMarket.GammaStartDate,
            externalMarket.EventStartsAt.Value,
            externalMarket.EventEndsAt.Value,
            externalMarket.ExternalClosedAt,
            now);
        if (marketResult.IsFailure)
            return marketResult.Error;

        foreach (var externalToken in externalMarket.Tokens)
        {
            var tokenIdResult = TokenId.Create(externalToken.TokenId);
            if (tokenIdResult.IsFailure)
                return tokenIdResult.Error;

            var addTokenResult = marketResult.Value.AddToken(
                tokenIdResult.Value,
                externalToken.Outcome,
                externalToken.OutcomeIndex);
            if (addTokenResult.IsFailure)
                return addTokenResult.Error;
        }

        return marketResult.Value;
    }

    private async Task<IdentityResolution> ResolveIdentityAsync(
        MarketAggregate candidate,
        CancellationToken cancellationToken)
    {
        var markets = new List<MarketAggregate?>
        {
            await marketRepository.GetByEventSlugAsync(candidate.EventSlug, cancellationToken),
            await marketRepository.GetByExternalEventIdAsync(candidate.ExternalEventId, cancellationToken),
            await marketRepository.GetBySlugAsync(candidate.MarketSlug, cancellationToken),
            await marketRepository.GetByExternalIdAsync(candidate.ExternalMarketId, cancellationToken),
            await marketRepository.GetByConditionIdAsync(candidate.ConditionId, cancellationToken)
        };
        markets.AddRange(await marketRepository.GetByAnyTokenIdsAsync(
            candidate.Tokens.Select(token => token.ExternalTokenId).ToArray(),
            cancellationToken));
        var resolvedMarkets = markets
            .Where(market => market is not null)
            .Cast<MarketAggregate>()
            .ToArray();

        if (resolvedMarkets.Length == 0)
            return new IdentityResolution(null, false);

        var existing = resolvedMarkets[0];
        if (resolvedMarkets.Any(market => !market.Id.Equals(existing.Id)))
            return new IdentityResolution(null, true);

        return existing.HasSameIdentity(candidate)
            ? new IdentityResolution(existing, false)
            : new IdentityResolution(null, true);
    }

    private async Task<Result<RegisterMarketResponse, ErrorList>> RefreshExistingAsync(
        MarketAggregate existing,
        MarketAggregate candidate,
        CancellationToken cancellationToken)
    {
        var refreshResult = existing.RefreshSchedule(
            candidate.ExternalCreatedAt,
            candidate.OrdersOpenedAt,
            candidate.GammaStartDate,
            candidate.EventStartsAt,
            candidate.EventEndsAt,
            candidate.ExternalClosedAt,
            candidate.ScheduleRefreshedAt);
        if (refreshResult.IsFailure)
            return Failure(refreshResult.Error);

        var updateResult = await marketRepository.UpdateScheduleAsync(existing, cancellationToken);
        return updateResult.IsSuccess
            ? Existing(existing)
            : Failure(updateResult.Error);
    }

    private static Result<RegisterMarketResponse, ErrorList> Existing(MarketAggregate market)
    {
        return new RegisterMarketResponse(market.Id.Value, false);
    }

    private static Result<RegisterMarketResponse, ErrorList> Failure(Error error)
    {
        return Result.Failure<RegisterMarketResponse, ErrorList>(error);
    }

    private sealed record IdentityResolution(MarketAggregate? Market, bool Conflict);
}
