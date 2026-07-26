using PolymarketLab.Markets.Contracts;
using PolymarketLab.Markets.Core.Ports;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.Markets.Core.Application.Integration;

internal sealed class MarketsReader(IMarketRepository repository) : IMarketsReader
{
    public async Task<MarketForCollection?> GetForCollectionAsync(
        MarketId marketId,
        CancellationToken cancellationToken)
    {
        var market = await repository.GetByIdAsync(marketId, cancellationToken);

        if (market is null)
            return null;

        return new MarketForCollection(
            market.Id,
            market.Slug.Value,
            market.Tokens
                .Select(token => new MarketTokenForCollection(
                    token.ExternalTokenId,
                    token.Outcome,
                    token.OutcomeIndex))
                .ToArray());
    }
}
