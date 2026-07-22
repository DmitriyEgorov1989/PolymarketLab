using CSharpFunctionalExtensions;
using PolymarketLab.Markets.Core.Domain.Models.Market.MarketAggregate;
using PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.Markets.Core.Ports;

public interface IMarketRepository
{
    Task<Market?> GetBySlugAsync(
        MarketSlug slug,
        CancellationToken cancellationToken);

    Task<Market?> GetByExternalIdAsync(
        ExternalMarketId externalMarketId,
        CancellationToken cancellationToken);

    Task<Market?> GetByConditionIdAsync(
        ConditionId conditionId,
        CancellationToken cancellationToken);

    Task<Result<MarketInsertStatus, Error>> TryAddAsync(
        Market market,
        CancellationToken cancellationToken);
}
