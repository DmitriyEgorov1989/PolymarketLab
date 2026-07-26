using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports
{
    public interface IMarketCollectionSource
    {
        Task<CollectionMarket?> GetByIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken);
    }
}
