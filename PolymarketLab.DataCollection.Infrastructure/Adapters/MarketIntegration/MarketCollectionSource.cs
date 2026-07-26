using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.Markets.Contracts;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.MarketIntegration;

internal sealed class MarketCollectionSource(IMarketsReader marketsReader)
    : IMarketCollectionSource
{
    public async Task<CollectionMarket?> GetByIdAsync(
        MarketId marketId,
        CancellationToken cancellationToken)
    {
        var market = await marketsReader.GetForCollectionAsync(
            marketId,
            cancellationToken);

        if (market is null)
            return null;

        return new CollectionMarket(
            market.MarketId,
            market.Slug,
            market.Tokens
                .Select(token => new CollectionMarketToken(
                    token.TokenId,
                    token.Outcome,
                    token.OutcomeIndex))
                .ToArray());
    }
}