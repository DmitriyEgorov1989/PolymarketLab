using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.Markets.Contracts;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.MarketIntegration;

internal sealed class MarketCollectionSource(IMarketsReader marketsReader)
    : IMarketCollectionSource
{
    public async Task<CollectionMarketWindow?> GetWindowAsync(
        MarketId marketId,
        CancellationToken cancellationToken)
    {
        var window = await marketsReader.GetCollectionWindowAsync(
            marketId,
            cancellationToken);
        return window is null
            ? null
            : new CollectionMarketWindow(window.MarketId, window.EventStartsAt);
    }

    public async Task<Result<CollectionMarket?, Error>> GetByIdAsync(
        MarketId marketId,
        CancellationToken cancellationToken)
    {
        var market = await marketsReader.GetForCollectionAsync(
            marketId,
            cancellationToken);

        if (market.IsFailure)
            return market.Error;

        if (market.Value is null)
            return (CollectionMarket?)null;

        return new CollectionMarket(
            market.Value.MarketId,
            market.Value.ExternalEventId,
            market.Value.EventSlug,
            market.Value.ExternalMarketId,
            market.Value.MarketSlug,
            market.Value.ConditionId,
            market.Value.EventStartsAt,
            market.Value.EventEndsAt,
            market.Value.Active,
            market.Value.Closed,
            market.Value.AcceptingOrders,
            market.Value.OrderBookEnabled,
            market.Value.Tokens
                .Select(token => new CollectionMarketToken(
                    token.TokenId,
                    token.Outcome,
                    token.OutcomeIndex))
                .ToArray());
    }
}
