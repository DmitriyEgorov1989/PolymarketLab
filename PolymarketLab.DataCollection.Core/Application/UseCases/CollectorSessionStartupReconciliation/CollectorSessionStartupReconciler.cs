using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorFailedDatasetFinalization;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionInvalidation;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.SharedKernel.Errors;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionStartupReconciliation;

public sealed class CollectorSessionStartupReconciler(
    ICollectorSessionRepository sessionRepository,
    ICollectorSessionInvalidationCoordinator invalidationCoordinator,
    ICollectorFailedDatasetFinalizer failedDatasetFinalizer,
    TimeProvider timeProvider)
    : ICollectorSessionStartupReconciler
{
    public async Task<UnitResult<Error>> ReconcileAsync(
        CancellationToken cancellationToken)
    {
        var activeSessions = await sessionRepository.GetActiveAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();

        foreach (var session in activeSessions)
        {
            if (IsFutureScheduled(session, now))
                continue;

            var result = await invalidationCoordinator.InvalidateAsync(
                session.Id,
                now,
                CollectorStopReason.ProcessTerminated,
                CollectorSessionStartupReconciliationErrors.ProcessTerminated,
                cancellationToken);
            if (result.IsFailure)
                return UnitResult.Failure(result.Error);
            if (result.Value is null)
                continue;

            var finalization = await failedDatasetFinalizer.FinalizeAsync(
                result.Value,
                cancellationToken);
            if (finalization.IsFailure)
                return finalization;
        }

        var expiredSessions = await sessionRepository.GetExpiredRetainedAsync(
            now,
            cancellationToken);
        foreach (var session in expiredSessions)
        {
            var finalization = await failedDatasetFinalizer.FinalizeAsync(
                session,
                cancellationToken);
            if (finalization.IsFailure)
                return finalization;
        }

        return UnitResult.Success<Error>();
    }

    private static bool IsFutureScheduled(
        CollectorSessionAggregate session,
        DateTimeOffset now) =>
        session.Status == CollectorSessionStatus.Scheduled
        && session.EventStartsAt is not null
        && now < session.EventStartsAt.Value;
}
