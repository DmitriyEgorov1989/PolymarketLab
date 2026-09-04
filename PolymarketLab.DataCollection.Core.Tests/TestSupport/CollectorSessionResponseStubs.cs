using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Tests.TestSupport;

/// <summary>Stub readiness-репозитория с необязательным набором наблюдений.</summary>
internal sealed class StubCollectorTokenReadinessRepository(
    IReadOnlyCollection<CollectorTokenReadiness>? readiness = null)
    : ICollectorTokenReadinessRepository
{
    public Task RecordInitialBookEnqueuedAsync(
        CollectorTokenReadiness readiness,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyCollection<CollectorTokenReadiness>> GetAsync(
        CollectorSessionId sessionId,
        long connectionEpoch,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<CollectorTokenReadiness>>(
            readiness ?? []);
}

/// <summary>Stub resolution-репозитория с необязательным durable state.</summary>
internal sealed class StubResolutionObservationRepository(
    DurableResolutionState? state = null)
    : IResolutionObservationRepository
{
    public Task<DurableResolutionState> GetStateAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken) =>
        Task.FromResult(state ?? new DurableResolutionState(sessionId, 0, null, null, []));

    public Task SaveWebSocketScanAsync(
        DurableWebSocketResolutionScan scan,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<long> SaveGammaObservationAsync(
        CollectorSessionId sessionId,
        GammaTerminalResolutionObservation observation,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<long> SaveClobObservationAsync(
        CollectorSessionId sessionId,
        ClobTerminalResolutionObservation observation,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<long> SaveFailureAsync(
        DurableResolutionFailure failure,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task RecordPollingCycleAsync(
        CollectorSessionId sessionId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetConfirmationReferenceAsync(
        CollectorSessionId sessionId,
        ResolutionConfirmationReference confirmation,
        CancellationToken cancellationToken) => throw new NotSupportedException();
}

/// <summary>Stub read-порта audit очистки с необязательным audit.</summary>
internal sealed class StubCollectorDatasetCleanupAuditReader(
    CollectorDatasetCleanupAudit? audit = null)
    : ICollectorDatasetCleanupAuditReader
{
    public Task<CollectorDatasetCleanupAudit?> GetBySessionIdAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken) => Task.FromResult(audit);
}

/// <summary>Stub read-порта нормализации с необязательным suitability.</summary>
internal sealed class StubNormalizationSuitabilityReader(
    NormalizationSuitability? suitability = null)
    : INormalizationSuitabilityReader
{
    private static readonly NormalizationSuitability EmptySuitability =
        new(0, 0, 0, 0, 0, 0, 0, 0, false);

    public Task<NormalizationSuitability> ReadAsync(
        CollectorSessionId sessionId,
        int projectionVersion,
        CancellationToken cancellationToken) =>
        Task.FromResult(suitability ?? EmptySuitability);
}
