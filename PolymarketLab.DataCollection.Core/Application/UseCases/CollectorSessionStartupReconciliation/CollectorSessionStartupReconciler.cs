using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.SharedKernel.Errors;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionStartupReconciliation;

public sealed class CollectorSessionStartupReconciler(
    ICollectorSessionRepository sessionRepository,
    TimeProvider timeProvider)
    : ICollectorSessionStartupReconciler
{
    private const int MaximumUpdateAttempts = 3;

    public async Task<UnitResult<Error>> ReconcileAsync(
        CancellationToken cancellationToken)
    {
        var activeSessions = await sessionRepository.GetActiveAsync(cancellationToken);

        foreach (var session in activeSessions)
        {
            var result = await ReconcileSessionAsync(session, cancellationToken);
            if (result.IsFailure)
                return result;
        }

        return UnitResult.Success<Error>();
    }

    private async Task<UnitResult<Error>> ReconcileSessionAsync(
        CollectorSessionAggregate initialSession,
        CancellationToken cancellationToken)
    {
        CollectorSessionAggregate? session = initialSession;

        for (var attempt = 0; attempt < MaximumUpdateAttempts; attempt++)
        {
            if (session is null || !IsActive(session.Status))
                return UnitResult.Success<Error>();

            var expectedStatus = session.Status;
            var lowerBound = session.StartedAt ?? session.CreatedAt;
            var currentTime = timeProvider.GetUtcNow();
            var interruptedAt = currentTime < lowerBound
                ? lowerBound
                : currentTime;
            var interruptResult = session.Interrupt(
                interruptedAt,
                CollectorStopReason.ProcessTerminated);
            if (interruptResult.IsFailure)
                return interruptResult;

            var updateResult = await sessionRepository.TryUpdateAsync(
                session,
                expectedStatus,
                cancellationToken);
            if (updateResult.IsFailure)
                return UnitResult.Failure(updateResult.Error);

            if (updateResult.Value == CollectorSessionUpdateStatus.Updated)
                return UnitResult.Success<Error>();

            session = await sessionRepository.GetByIdAsync(
                initialSession.Id,
                cancellationToken);
        }

        if (session is null || !IsActive(session.Status))
            return UnitResult.Success<Error>();

        return UnitResult.Failure(
            CollectorSessionStartupReconciliationErrors.StateTransitionConflict(
                initialSession.Id));
    }

    private static bool IsActive(CollectorSessionStatus status)
    {
        return status is CollectorSessionStatus.Scheduled
            or CollectorSessionStatus.Starting
            or CollectorSessionStatus.Running
            or CollectorSessionStatus.Stopping
            or CollectorSessionStatus.Invalidating;
    }
}
