using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports.Enums;
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

    Task<CollectorSession?> GetCurrentByMarketIdAsync(
        MarketId marketId,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<CollectorSession>> GetActiveAsync(
        CancellationToken cancellationToken);

    Task<Result<CollectorSessionInsertStatus, Error>> TryAddAsync(
        CollectorSession session,
        CancellationToken cancellationToken);

    Task<Result<CollectorSessionUpdateStatus, Error>> TryUpdateAsync(
        CollectorSession session,
        CollectorSessionStatus expectedStatus,
        CancellationToken cancellationToken);
}
