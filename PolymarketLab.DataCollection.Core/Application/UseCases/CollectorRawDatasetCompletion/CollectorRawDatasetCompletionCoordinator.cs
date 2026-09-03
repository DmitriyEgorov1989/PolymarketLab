using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionInvalidation;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorRawDatasetCompletion;

/// <summary>
/// Выполняет controlled drain подтверждённой session: CAS-переводит её в
/// <c>Stopping/DrainingRaw</c>, останавливает producer, дожидается durable хвоста,
/// проверяет точное равенство <c>received = enqueued = persisted = raw &gt; 0</c>
/// и только затем CAS-переводит session в <c>Stopping/AwaitingNormalization</c>.
/// </summary>
public sealed class CollectorRawDatasetCompletionCoordinator(
    ICollectorSessionRepository sessionRepository,
    ICollectorRuntime runtime,
    ICollectorSessionProgressCompletion progressCompletion,
    ICollectorSessionProgressRepository progressRepository,
    ICollectorSessionInvalidationCoordinator invalidationCoordinator,
    TimeProvider timeProvider,
    ILogger<CollectorRawDatasetCompletionCoordinator> logger)
    : ICollectorRawDatasetCompletionCoordinator
{
    private const int MaximumUpdateAttempts = 3;

    /// <inheritdoc />
    public async Task<UnitResult<Error>> CompleteAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken)
    {
        var draining = await MarkDrainingRawAsync(sessionId, cancellationToken);
        if (draining.IsFailure)
            return await InvalidateAndStopAsync(
                sessionId,
                draining.Error,
                cancellationToken);

        var stop = await runtime.StopAsync(sessionId, cancellationToken);
        if (stop.IsFailure)
            return await InvalidateAndStopAsync(sessionId, stop.Error, cancellationToken);

        var drain = await progressCompletion.CompleteAsync(sessionId, cancellationToken);
        if (drain.IsFailure)
            return await InvalidateAndStopAsync(sessionId, drain.Error, cancellationToken);

        CollectorSessionProgress progress;
        try
        {
            progress = await progressRepository.GetAsync(sessionId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Failed to read final raw accounting for collector session {SessionId}.",
                sessionId.Value);
            return await InvalidateAndStopAsync(
                sessionId,
                CollectorRawDatasetCompletionErrors.ProgressReadFailed(sessionId),
                cancellationToken);
        }

        if (!HasExactRawDataset(progress))
        {
            return await InvalidateAndStopAsync(
                sessionId,
                CollectorRawDatasetCompletionErrors.AccountingMismatch(progress),
                cancellationToken);
        }

        var awaitingNormalization = await MarkAwaitingNormalizationAsync(
            sessionId,
            cancellationToken);
        return awaitingNormalization.IsFailure
            ? await InvalidateAndStopAsync(
                sessionId,
                awaitingNormalization.Error,
                cancellationToken)
            : UnitResult.Success<Error>();
    }

    private async Task<Result<CollectorSessionAggregate, Error>> MarkDrainingRawAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken)
    {
        var session = await sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        if (session is null)
            return CollectorRawDatasetCompletionErrors.SessionNotFound(sessionId);

        for (var attempt = 0; attempt < MaximumUpdateAttempts; attempt++)
        {
            if (session.Status == CollectorSessionStatus.Stopping
                && session.Phase is CollectorSessionPhase.DrainingRaw
                    or CollectorSessionPhase.AwaitingNormalization)
            {
                return session;
            }

            if (session.Status != CollectorSessionStatus.Running
                || session.Phase != CollectorSessionPhase.AwaitingResolution)
            {
                return CollectorRawDatasetCompletionErrors.StateTransitionConflict(sessionId);
            }

            if (session.ResolutionConfirmedAt is null)
                return CollectorRawDatasetCompletionErrors.ResolutionNotConfirmed(sessionId);

            var transition = session.MarkStopping();
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

            var current = await sessionRepository.GetByIdAsync(sessionId, cancellationToken);
            if (current is null)
                return CollectorRawDatasetCompletionErrors.SessionNotFound(sessionId);
            session = current;
        }

        return CollectorRawDatasetCompletionErrors.StateTransitionConflict(sessionId);
    }

    private async Task<Result<CollectorSessionAggregate, Error>> MarkAwaitingNormalizationAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken)
    {
        var session = await sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        if (session is null)
            return CollectorRawDatasetCompletionErrors.SessionNotFound(sessionId);

        for (var attempt = 0; attempt < MaximumUpdateAttempts; attempt++)
        {
            if (session.Status == CollectorSessionStatus.Stopping
                && session.Phase == CollectorSessionPhase.AwaitingNormalization)
            {
                return session;
            }

            if (session.Status != CollectorSessionStatus.Stopping
                || session.Phase != CollectorSessionPhase.DrainingRaw)
            {
                return CollectorRawDatasetCompletionErrors.StateTransitionConflict(sessionId);
            }

            var transition = session.MarkAwaitingNormalization();
            if (transition.IsFailure)
                return transition.Error;

            var update = await sessionRepository.TryUpdateAsync(
                session,
                CollectorSessionStatus.Stopping,
                cancellationToken);
            if (update.IsFailure)
                return update.Error;
            if (update.Value == CollectorSessionUpdateStatus.Updated)
                return session;

            var current = await sessionRepository.GetByIdAsync(sessionId, cancellationToken);
            if (current is null)
                return CollectorRawDatasetCompletionErrors.SessionNotFound(sessionId);
            session = current;
        }

        return CollectorRawDatasetCompletionErrors.StateTransitionConflict(sessionId);
    }

    private static bool HasExactRawDataset(CollectorSessionProgress progress) =>
        progress.MessagesReceived > 0
        && progress.MessagesReceived == progress.MessagesEnqueued
        && progress.MessagesReceived == progress.MessagesPersisted
        && progress.MessagesReceived == progress.RawMessageCount;

    private async Task<UnitResult<Error>> InvalidateAndStopAsync(
        CollectorSessionId sessionId,
        Error failure,
        CancellationToken cancellationToken)
    {
        var invalidation = await invalidationCoordinator.InvalidateAsync(
            sessionId,
            timeProvider.GetUtcNow(),
            CollectorStopReason.PersistenceFailure,
            failure,
            cancellationToken);
        if (invalidation.IsFailure)
            return UnitResult.Failure(invalidation.Error);

        if (invalidation.Value is null)
            return UnitResult.Failure(failure);

        var stop = await runtime.StopAsync(sessionId, cancellationToken);
        if (stop.IsFailure && stop.Error != failure)
        {
            logger.LogError(
                "Collector runtime stop after invalidation failed for session {SessionId}: " +
                "{ErrorCode}: {ErrorMessage}",
                sessionId.Value,
                stop.Error.Code,
                stop.Error.Message);
        }

        return UnitResult.Failure(failure);
    }
}
