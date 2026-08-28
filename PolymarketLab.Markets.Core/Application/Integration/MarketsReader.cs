using CSharpFunctionalExtensions;
using PolymarketLab.Markets.Contracts;
using PolymarketLab.Markets.Core.Application.Errors;
using PolymarketLab.Markets.Core.Ports;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.Markets.Core.Application.Integration;

internal sealed class MarketsReader(
    IMarketRepository repository,
    IExternalMarketGateway externalMarketGateway) : IMarketsReader
{
    public async Task<Result<MarketForCollection?, Error>> GetForCollectionAsync(
        MarketId marketId,
        CancellationToken cancellationToken)
    {
        var market = await repository.GetByIdAsync(marketId, cancellationToken);

        if (market is null)
            return (MarketForCollection?)null;

        var externalEventResult = await externalMarketGateway.GetByEventSlugAsync(
            market.EventSlug,
            cancellationToken);
        if (externalEventResult.IsFailure)
            return externalEventResult.Error;

        var externalEvent = externalEventResult.Value;
        var externalMarket = externalEvent.Market;

        if (!HasSameSnapshot(market, externalEvent)
            || MarketAvailability.IsTerminal(externalMarket))
        {
            return MarketCollectionErrors.Unavailable(market.Id.Value);
        }

        return new MarketForCollection(
            market.Id,
            externalEvent.ExternalEventId,
            externalEvent.Slug,
            externalMarket.ExternalMarketId,
            externalMarket.Slug,
            externalMarket.ConditionId,
            externalMarket.EventStartsAt!.Value.ToUniversalTime(),
            externalMarket.EventEndsAt!.Value.ToUniversalTime(),
            externalMarket.Active,
            externalMarket.Closed,
            externalMarket.AcceptingOrders,
            externalMarket.OrderBookEnabled,
            externalMarket.Tokens
                .OrderBy(token => token.OutcomeIndex)
                .Select(token => new MarketTokenForCollection(
                    TokenId.Create(token.TokenId).Value,
                    token.Outcome,
                    token.OutcomeIndex))
                .ToArray());
    }

    private static bool HasSameSnapshot(
        Domain.Models.Market.MarketAggregate.Market market,
        Ports.Dto.ExternalEvent externalEvent)
    {
        var externalMarket = externalEvent.Market;
        if (!string.Equals(market.ExternalEventId.Value, externalEvent.ExternalEventId, StringComparison.Ordinal)
            || !string.Equals(market.EventSlug.Value, externalEvent.Slug, StringComparison.Ordinal)
            || !string.Equals(market.ExternalMarketId.Value, externalMarket.ExternalMarketId, StringComparison.Ordinal)
            || !string.Equals(market.MarketSlug.Value, externalMarket.Slug, StringComparison.Ordinal)
            || !string.Equals(market.ConditionId.Value, externalMarket.ConditionId, StringComparison.Ordinal)
            || externalMarket.EventStartsAt is null
            || externalMarket.EventEndsAt is null
            || market.EventStartsAt != externalMarket.EventStartsAt.Value
            || market.EventEndsAt != externalMarket.EventEndsAt.Value)
        {
            return false;
        }

        var storedTokens = market.Tokens
            .OrderBy(token => token.OutcomeIndex)
            .Select(token => (token.ExternalTokenId.Value, token.Outcome, token.OutcomeIndex));
        var externalTokens = externalMarket.Tokens
            .OrderBy(token => token.OutcomeIndex)
            .Select(token => (token.TokenId, token.Outcome, token.OutcomeIndex));

        return storedTokens.SequenceEqual(externalTokens);
    }
}
