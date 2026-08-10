using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.Markets.Contracts;

public interface IMarketsReader
{
    Task<Result<MarketForCollection?, Error>> GetForCollectionAsync(
        MarketId marketId,
        CancellationToken cancellationToken);
}
