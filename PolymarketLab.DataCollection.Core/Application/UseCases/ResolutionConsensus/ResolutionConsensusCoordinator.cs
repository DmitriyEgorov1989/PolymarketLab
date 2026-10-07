using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorNormalizationSuitability;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorRawDatasetCompletion;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionInvalidation;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.SharedKernel.Errors;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.ResolutionConsensus;

/// <summary>
/// Продвигает активные сессии через границы окна, controlled raw drain и
/// проверку полностью нормализованного dataset. Определение победителя не входит
/// в lifecycle collector.
/// </summary>
public sealed class ResolutionConsensusCoordinator(
    ICollectorSessionRepository sessionRepository,
    ICollectorRuntime runtime,
    ICollectorSessionInvalidationCoordinator invalidationCoordinator,
    ICollectorRawDatasetCompletionCoordinator rawDatasetCompletion,
    ICollectorNormalizationSuitabilityCoordinator normalizationSuitabilityCoordinator,
    TimeProvider timeProvider) : IResolutionConsensusCoordinator
{
    private const int MaximumUpdateAttempts = 3;
    private readonly SemaphoreSlim _tickGate = new(1, 1);

    /// <inheritdoc />
    public async Task<UnitResult<Error>> TickAsync(CancellationToken cancellationToken)
    {
        if (!await _tickGate.WaitAsync(0, cancellationToken))
            return UnitResult.Success<Error>();

        try
        {
            return await TickCoreAsync(cancellationToken);
        }
        finally
        {
            _tickGate.Release();
        }
    }

    private async Task<UnitResult<Error>> TickCoreAsync(CancellationToken cancellationToken)
    {
        var sessions = await sessionRepository.GetActiveAsync(cancellationToken);
        Error? firstError = null;
        foreach (var session in sessions)
        {
            var result = await TickSessionAsync(session, cancellationToken);
            if (result.IsFailure && firstError is null)
                firstError = result.Error;
        }

        return firstError is null
            ? UnitResult.Success<Error>()
            : UnitResult.Failure(firstError);
    }

    private async Task<UnitResult<Error>> TickSessionAsync(
        CollectorSessionAggregate session,
        CancellationToken cancellationToken)
    {
        if (session.Status == CollectorSessionStatus.Stopping
            && session.Phase == CollectorSessionPhase.AwaitingNormalization)
        {
            return await normalizationSuitabilityCoordinator.EvaluateAsync(
                session.Id,
                cancellationToken);
        }

        if (session.Status != CollectorSessionStatus.Running)
            return UnitResult.Success<Error>();

        var now = timeProvider.GetUtcNow();
        if (!HasCollectionSnapshot(session))
            return await InvalidateAsync(session, now, cancellationToken);
        if (now < session.EventStartsAt!.Value)
            return UnitResult.Success<Error>();

        if (session.Phase == CollectorSessionPhase.ReadyBeforeWindow)
        {
            var collecting = await MarkCollectingWindowAsync(session, cancellationToken);
            if (collecting.IsFailure)
                return UnitResult.Failure(collecting.Error);
            session = collecting.Value;
        }

        if (now >= session.EventEndsAt!.Value
            && session.Phase is CollectorSessionPhase.CollectingWindow
                or CollectorSessionPhase.AwaitingResolution)
        {
            return await rawDatasetCompletion.CompleteAsync(
                session.Id,
                cancellationToken);
        }

        return UnitResult.Success<Error>();
    }

    private async Task<Result<CollectorSessionAggregate, Error>> MarkCollectingWindowAsync(
        CollectorSessionAggregate initialSession,
        CancellationToken cancellationToken)
    {
        var session = initialSession;
        for (var attempt = 0; attempt < MaximumUpdateAttempts; attempt++)
        {
            if (session.Status != CollectorSessionStatus.Running
                || session.Phase == CollectorSessionPhase.CollectingWindow)
            {
                return session;
            }

            var transition = session.MarkCollectingWindow();
            if (transition.IsFailure)
                return transition.Error;

            var update = await sessionRepository.TryUpdateAsync(
                session,
                CollectorSessionStatus.Running,
                cancellationToken);
            if (update.IsFailure)
                return update.Error;
            if (update.Value == CollectorSessionUpdateStatus.Updated)
                return session;

            var current = await sessionRepository.GetByIdAsync(
                session.Id,
                cancellationToken);
            if (current is null)
                return StateTransitionConflict(session.Id.Value);
            session = current;
        }

        return StateTransitionConflict(session.Id.Value);
    }

    private async Task<UnitResult<Error>> InvalidateAsync(
        CollectorSessionAggregate session,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var failure = new Error(
            "collector.lifecycle.snapshot_invalid",
            $"Collector session '{session.Id.Value}' has an invalid collection snapshot.",
            ErrorType.Conflict);
        var result = await invalidationCoordinator.InvalidateAsync(
            session.Id,
            occurredAt,
            CollectorStopReason.PersistenceFailure,
            failure,
            cancellationToken);
        if (result.IsFailure)
            return UnitResult.Failure(result.Error);

        return await runtime.StopAsync(session.Id, cancellationToken);
    }

    private static bool HasCollectionSnapshot(CollectorSessionAggregate session) =>
        session.EventStartsAt is not null
        && session.EventEndsAt is not null
        && session.Tokens.Count > 0;

    private static Error StateTransitionConflict(Guid sessionId) => new(
        "collector.lifecycle.state_transition_conflict",
        $"Collector session '{sessionId}' changed concurrently during lifecycle progression.",
        ErrorType.Conflict);
}
