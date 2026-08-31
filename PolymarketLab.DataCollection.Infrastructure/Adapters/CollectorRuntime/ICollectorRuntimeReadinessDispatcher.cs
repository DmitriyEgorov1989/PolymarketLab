using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;

internal interface ICollectorRuntimeReadinessDispatcher
{
    Task<UnitResult<Error>> MarkAwaitingInitialBooksAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken);

    Task<UnitResult<Error>> MarkAwaitingHeartbeatAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken);

    Task<UnitResult<Error>> MarkRunningAsync(
        CollectorSessionId sessionId,
        DateTimeOffset subscriptionReadyAt,
        CancellationToken cancellationToken);

    Task<UnitResult<Error>> BeginInvalidationAsync(
        CollectorSessionId sessionId,
        Error failure,
        CancellationToken cancellationToken);
}
