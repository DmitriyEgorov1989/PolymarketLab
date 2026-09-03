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

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorNormalizationSuitability;

/// <summary>
/// Доказывает пригодность normalized dataset snapshot-версии session: сравнивает
/// snapshot <c>ProjectionVersion</c> с активной runtime-версией, одним persistence
/// read проверяет точную Processed cardinality и strict WS resolution provenance,
/// ожидает незавершённую обработку до deadline
/// <c>AwaitingNormalizationAt + 5 минут</c>,
/// инвалидирует любой недоказанный dataset и завершает session как
/// <c>Stopped/MarketClosed</c> только при полном доказательстве пригодности.
/// </summary>
public sealed class CollectorNormalizationSuitabilityCoordinator(
    ICollectorSessionRepository sessionRepository,
    INormalizationSuitabilityReader suitabilityReader,
    IProjectionVersionProvider projectionVersionProvider,
    ICollectorSessionInvalidationCoordinator invalidationCoordinator,
    TimeProvider timeProvider,
    ILogger<CollectorNormalizationSuitabilityCoordinator> logger)
    : ICollectorNormalizationSuitabilityCoordinator
{
    private static readonly TimeSpan NormalizationTimeout = TimeSpan.FromMinutes(5);
    private const int MaximumUpdateAttempts = 3;

    /// <inheritdoc />
    public async Task<UnitResult<Error>> EvaluateAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken)
    {
        var session = await sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        if (session is null)
            return UnitResult.Failure(
                CollectorNormalizationSuitabilityErrors.SessionNotFound(sessionId));

        if (IsStoppedAsMarketClosed(session))
            return UnitResult.Success<Error>();

        if (session.Status is CollectorSessionStatus.Invalidating
            or CollectorSessionStatus.Failed)
        {
            return UnitResult.Success<Error>();
        }

        if (session.Status != CollectorSessionStatus.Stopping
            || session.Phase != CollectorSessionPhase.AwaitingNormalization)
        {
            return await InvalidateAndFailAsync(
                sessionId,
                CollectorNormalizationSuitabilityErrors.StateTransitionConflict(sessionId),
                cancellationToken);
        }

        if (session.ProjectionVersion is not > 0)
        {
            return await InvalidateAndFailAsync(
                sessionId,
                CollectorNormalizationSuitabilityErrors.ProjectionVersionMissing(sessionId),
                cancellationToken);
        }

        var snapshotVersion = session.ProjectionVersion.Value;
        var runtimeVersion = projectionVersionProvider.ProjectionVersion;
        if (snapshotVersion != runtimeVersion)
        {
            return await InvalidateAndFailAsync(
                sessionId,
                CollectorNormalizationSuitabilityErrors.ProjectionVersionMismatch(
                    sessionId,
                    snapshotVersion,
                    runtimeVersion),
                cancellationToken);
        }

        if (session.AwaitingNormalizationAt is null)
        {
            return await InvalidateAndFailAsync(
                sessionId,
                CollectorNormalizationSuitabilityErrors.AwaitingNormalizationAtMissing(sessionId),
                cancellationToken);
        }

        var deadline = session.AwaitingNormalizationAt.Value + NormalizationTimeout;
        if (timeProvider.GetUtcNow() >= deadline)
        {
            return await InvalidateAndFailAsync(
                sessionId,
                CollectorNormalizationSuitabilityErrors.Timeout(sessionId, deadline),
                cancellationToken);
        }

        NormalizationSuitability suitability;
        try
        {
            suitability = await suitabilityReader.ReadAsync(
                sessionId,
                snapshotVersion,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Failed to read normalization suitability for collector session {SessionId}.",
                sessionId.Value);
            return await InvalidateAndFailAsync(
                sessionId,
                CollectorNormalizationSuitabilityErrors.ReadFailed(sessionId),
                cancellationToken);
        }

        if (suitability.UnsupportedCount > 0)
        {
            return await InvalidateAndFailAsync(
                sessionId,
                CollectorNormalizationSuitabilityErrors.Unsupported(
                    sessionId,
                    suitability.UnsupportedCount),
                cancellationToken);
        }

        if (suitability.InvalidCount > 0)
        {
            return await InvalidateAndFailAsync(
                sessionId,
                CollectorNormalizationSuitabilityErrors.Invalid(
                    sessionId,
                    suitability.InvalidCount),
                cancellationToken);
        }

        if (suitability.FailedCount > 0)
        {
            return await InvalidateAndFailAsync(
                sessionId,
                CollectorNormalizationSuitabilityErrors.Failed(
                    sessionId,
                    suitability.FailedCount),
                cancellationToken);
        }

        if (IsFullyProcessed(suitability))
        {
            if (!suitability.ResolutionRawItemProcessed)
            {
                return await InvalidateAndFailAsync(
                    sessionId,
                    CollectorNormalizationSuitabilityErrors.ResolutionProvenanceInvalid(sessionId),
                    cancellationToken);
            }

            var completion = await StopAsMarketClosedAsync(
                session,
                timeProvider.GetUtcNow(),
                cancellationToken);
            return completion.IsSuccess
                ? UnitResult.Success<Error>()
                : await InvalidateAndFailAsync(sessionId, completion.Error, cancellationToken);
        }

        return UnitResult.Success<Error>();
    }

    private async Task<Result<CollectorSessionAggregate, Error>> StopAsMarketClosedAsync(
        CollectorSessionAggregate initialSession,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var session = initialSession;
        for (var attempt = 0; attempt < MaximumUpdateAttempts; attempt++)
        {
            if (IsStoppedAsMarketClosed(session))
                return session;

            if (session.Status is CollectorSessionStatus.Invalidating
                or CollectorSessionStatus.Failed)
            {
                return session;
            }

            if (session.Status != CollectorSessionStatus.Stopping
                || session.Phase != CollectorSessionPhase.AwaitingNormalization)
            {
                return CollectorNormalizationSuitabilityErrors.StateTransitionConflict(session.Id);
            }

            var transition = session.Stop(now, CollectorStopReason.MarketClosed);
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

            var current = await sessionRepository.GetByIdAsync(
                session.Id,
                cancellationToken);
            if (current is null)
                return CollectorNormalizationSuitabilityErrors.SessionNotFound(session.Id);
            session = current;
        }

        return CollectorNormalizationSuitabilityErrors.StateTransitionConflict(session.Id);
    }

    private async Task<UnitResult<Error>> InvalidateAndFailAsync(
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
        return invalidation.IsFailure
            ? UnitResult.Failure(invalidation.Error)
            : UnitResult.Failure(failure);
    }

    private static bool IsStoppedAsMarketClosed(CollectorSessionAggregate session) =>
        session.Status == CollectorSessionStatus.Stopped
        && session.StopReason == CollectorStopReason.MarketClosed;

    private static bool IsFullyProcessed(NormalizationSuitability value) =>
        value.RawCount > 0
        && value.LedgerCount == value.RawCount
        && value.ProcessedCount == value.RawCount
        && value.PendingCount == 0
        && value.ProcessingCount == 0
        && value.UnsupportedCount == 0
        && value.InvalidCount == 0
        && value.FailedCount == 0
        && value.MissingCount == 0;
}
