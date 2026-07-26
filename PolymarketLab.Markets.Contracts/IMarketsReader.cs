using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.Markets.Contracts;

public interface IMarketsReader
{
    Task<MarketForCollection?> GetForCollectionAsync(
        MarketId marketId,
        CancellationToken cancellationToken);
}
