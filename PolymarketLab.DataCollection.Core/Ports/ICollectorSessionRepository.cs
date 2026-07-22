using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Ports;

public interface ICollectorSessionRepository
{
    Task<CollectorSession?> GetByIdAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken);

    Task<CollectorSession?> GetActiveByMarketIdAsync(
        MarketId marketId,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<CollectorSession>> GetActiveAsync(
        CancellationToken cancellationToken);

    Task<Result<CollectorSessionInsertStatus, Error>> TryAddAsync(
        CollectorSession session,
        CancellationToken cancellationToken);

    Task<UnitResult<Error>> UpdateAsync(
        CollectorSession session,
        CancellationToken cancellationToken);
}
