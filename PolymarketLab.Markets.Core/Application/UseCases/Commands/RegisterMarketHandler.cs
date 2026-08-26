using CSharpFunctionalExtensions;
using MediatR;
using PolymarketLab.Markets.Core.Application.Errors;
using PolymarketLab.Markets.Core.Application.Extensions;
using PolymarketLab.Markets.Core.Application.Integration;
using PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects;
using PolymarketLab.Markets.Core.Ports;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using static PolymarketLab.SharedKernel.Errors.Error;
using MarketAggregate = PolymarketLab.Markets.Core.Domain.Models.Market.MarketAggregate.Market;

namespace PolymarketLab.Markets.Core.Application.UseCases.Commands
{
    public sealed class RegisterMarketHandler(
        IExternalMarketGateway externalMarketGateway,
        IMarketRepository marketRepository)
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
            var externalEventSlugResult = EventSlug.Create(externalEvent.Slug);
            if (externalEventSlugResult.IsFailure)
                return Failure(externalEventSlugResult.Error);

            if (!requestedEventSlug.Equals(externalEventSlugResult.Value))
            {
                return Failure(MarketRegistrationErrors.EventSlugMismatch(
                    requestedEventSlug.Value,
                    externalEventSlugResult.Value.Value));
            }

            var externalMarket = externalEvent.Market;

            var externalIdResult = ExternalMarketId.Create(externalMarket.ExternalMarketId);
            if (externalIdResult.IsFailure)
                return Failure(externalIdResult.Error);

            var externalSlugResult = MarketSlug.Create(externalMarket.Slug);
            if (externalSlugResult.IsFailure)
                return Failure(externalSlugResult.Error);

            var conditionIdResult = ConditionId.Create(externalMarket.ConditionId);
            if (conditionIdResult.IsFailure)
                return Failure(conditionIdResult.Error);

            if (string.IsNullOrWhiteSpace(externalMarket.Question))
                return Failure(GeneralErrors.ValueIsRequired(nameof(externalMarket.Question)));

            var externalId = externalIdResult.Value;
            var externalSlug = externalSlugResult.Value;
            var conditionId = conditionIdResult.Value;
            var existingBySlug = await marketRepository.GetBySlugAsync(externalSlug, cancellationToken);
            var existingByExternalId = await marketRepository.GetByExternalIdAsync(
                externalId,
                cancellationToken);
            var existingByConditionId = await marketRepository.GetByConditionIdAsync(
                conditionId,
                cancellationToken);

            var identity = ResolveIdentity(
                existingBySlug,
                existingByExternalId,
                existingByConditionId,
                externalSlug,
                externalId,
                conditionId);

            if (identity.Conflict)
                return Failure(MarketRegistrationErrors.IdentityConflict);

            if (identity.Market is not null)
                return Existing(identity.Market);

            if (!externalMarket.OrderBookEnabled)
                return Failure(MarketRegistrationErrors.OrderBookDisabled);

            if (!MarketAvailability.IsAvailable(externalMarket))
                return Failure(MarketRegistrationErrors.Unavailable);

            if (externalMarket.Tokens is null || externalMarket.Tokens.Count == 0)
                return Failure(MarketRegistrationErrors.TokensRequired);

            var marketIdResult = MarketId.Create(Guid.NewGuid());
            if (marketIdResult.IsFailure)
                return Failure(marketIdResult.Error);

            var marketResult = MarketAggregate.Create(
                marketIdResult.Value,
                externalId,
                externalSlug,
                conditionId,
                externalMarket.Question,
                externalMarket.StartsAt,
                externalMarket.EndsAt);

            if (marketResult.IsFailure)
                return Failure(marketResult.Error);

            var market = marketResult.Value;
            foreach (var externalToken in externalMarket.Tokens)
            {
                var tokenIdResult = TokenId.Create(externalToken.TokenId);
                if (tokenIdResult.IsFailure)
                    return Failure(tokenIdResult.Error);

                var addTokenResult = market.AddToken(
                    tokenIdResult.Value,
                    externalToken.Outcome,
                    externalToken.OutcomeIndex);

                if (addTokenResult.IsFailure)
                    return Failure(addTokenResult.Error);
            }

            var insertResult = await marketRepository.TryAddAsync(market, cancellationToken);
            if (insertResult.IsFailure)
                return Failure(insertResult.Error);

            if (insertResult.Value == MarketInsertStatus.Inserted)
                return new RegisterMarketResponse(market.Id.Value, true);

            if (insertResult.Value != MarketInsertStatus.UniqueConflict)
                return Failure(MarketRegistrationErrors.RaceUnresolved);

            existingBySlug = await marketRepository.GetBySlugAsync(externalSlug, cancellationToken);
            existingByExternalId = await marketRepository.GetByExternalIdAsync(externalId, cancellationToken);
            existingByConditionId = await marketRepository.GetByConditionIdAsync(conditionId, cancellationToken);

            identity = ResolveIdentity(
                existingBySlug,
                existingByExternalId,
                existingByConditionId,
                externalSlug,
                externalId,
                conditionId);

            if (identity.Conflict)
                return Failure(MarketRegistrationErrors.IdentityConflict);

            return identity.Market is not null
                ? Existing(identity.Market)
                : Failure(MarketRegistrationErrors.RaceUnresolved);
        }

        private static IdentityResolution ResolveIdentity(
            MarketAggregate? bySlug,
            MarketAggregate? byExternalId,
            MarketAggregate? byConditionId,
            MarketSlug slug,
            ExternalMarketId externalId,
            ConditionId conditionId)
        {
            var markets = new[] { bySlug, byExternalId, byConditionId }
                .Where(market => market is not null)
                .Cast<MarketAggregate>()
                .ToArray();

            if (markets.Length == 0)
                return new IdentityResolution(null, false);

            var existing = markets[0];
            if (markets.Any(market => !market.Id.Equals(existing.Id)))
                return new IdentityResolution(null, true);

            var sameIdentity = existing.Slug.Equals(slug)
                && existing.ExternalId.Equals(externalId)
                && existing.ConditionId.Equals(conditionId);

            return sameIdentity
                ? new IdentityResolution(existing, false)
                : new IdentityResolution(null, true);
        }

        private static Result<RegisterMarketResponse, ErrorList> Existing(MarketAggregate market)
        {
            return new RegisterMarketResponse(market.Id.Value, false);
        }

        private static Result<RegisterMarketResponse, ErrorList> Failure(Error error)
        {
            return Result.Failure<RegisterMarketResponse, ErrorList>(error);
        }

        private sealed record IdentityResolution(
            MarketAggregate? Market,
            bool Conflict);
    }
}
