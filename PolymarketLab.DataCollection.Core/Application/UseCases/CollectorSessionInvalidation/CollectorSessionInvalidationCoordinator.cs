using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionInvalidation;

/// <inheritdoc />
public sealed class CollectorSessionInvalidationCoordinator(
    ICollectorSessionRepository sessionRepository,
    ICollectorRuntime runtime) : ICollectorSessionInvalidationCoordinator
{
    private const int MaximumUpdateAttempts = 3;

    /// <inheritdoc />
    public async Task<Result<CollectorSessionAggregate?, Error>> InvalidateAsync(
        CollectorSessionId sessionId,
        DateTimeOffset occurredAt,
        CollectorStopReason reason,
        Error failure,
        CancellationToken cancellationToken)
    {
        runtime.FenceSession(sessionId);

        for (var attempt = 0; attempt < MaximumUpdateAttempts; attempt++)
        {
            var session = await sessionRepository.GetByIdAsync(
                sessionId,
                cancellationToken);
            if (session is null
                || session.Status == CollectorSessionStatus.Invalidating
                || IsTerminal(session.Status))
            {
                return session;
            }

            var expectedStatus = session.Status;
            var lowerBound = session.StartedAt ?? session.CreatedAt;
            var invalidatingAt = occurredAt < lowerBound ? lowerBound : occurredAt;
            var transition = session.BeginInvalidation(
                invalidatingAt,
                reason,
                failure.Code,
                failure.Message);
            if (transition.IsFailure)
                return transition.Error;

            var update = await sessionRepository.TryUpdateAsync(
                session,
                expectedStatus,
                cancellationToken);
            if (update.IsFailure)
                return update.Error;
            if (update.Value == CollectorSessionUpdateStatus.Updated)
                return session;
        }

        return CollectorInvalidationErrors.StateTransitionConflict(sessionId);
    }

    private static bool IsTerminal(CollectorSessionStatus status) => status is
        CollectorSessionStatus.Stopped
        or CollectorSessionStatus.Failed
        or CollectorSessionStatus.Interrupted;
}
