using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionShutdown;

public sealed class CollectorSessionShutdownHandler(
    ICollectorSessionRepository sessionRepository,
    TimeProvider timeProvider)
    : ICollectorSessionShutdownHandler
{
    private const int MaximumUpdateAttempts = 3;

    public Task<UnitResult<Error>> MarkStoppingAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken)
    {
        return UpdateSessionAsync(sessionId, false, cancellationToken);
    }

    public Task<UnitResult<Error>> MarkStoppedAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken)
    {
        return UpdateSessionAsync(sessionId, true, cancellationToken);
    }

    public Task<UnitResult<Error>> MarkFailedAsync(
        CollectorSessionId sessionId,
        Error error,
        CancellationToken cancellationToken)
    {
        return FailSessionAsync(sessionId, error, cancellationToken);
    }

    private async Task<UnitResult<Error>> UpdateSessionAsync(
        CollectorSessionId sessionId,
        bool completeStop,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaximumUpdateAttempts; attempt++)
        {
            var session = await sessionRepository.GetByIdAsync(
                sessionId,
                cancellationToken);
            if (session is null || IsTerminal(session.Status))
                return UnitResult.Success<Error>();

            if (!completeStop && session.Status == CollectorSessionStatus.Stopping)
                return UnitResult.Success<Error>();

            var expectedStatus = session.Status;
            UnitResult<Error> transitionResult;

            if (completeStop)
            {
                var lowerBound = session.StartedAt ?? session.CreatedAt;
                var currentTime = timeProvider.GetUtcNow();
                var stoppedAt = currentTime < lowerBound
                    ? lowerBound
                    : currentTime;
                transitionResult = session.Stop(
                    stoppedAt,
                    CollectorStopReason.ApplicationShutdown);
            }
            else
            {
                transitionResult = session.MarkStopping();
            }

            if (transitionResult.IsFailure)
                return transitionResult;

            var updateResult = await sessionRepository.TryUpdateAsync(
                session,
                expectedStatus,
                cancellationToken);
            if (updateResult.IsFailure)
                return UnitResult.Failure(updateResult.Error);

            if (updateResult.Value == CollectorSessionUpdateStatus.Updated)
                return UnitResult.Success<Error>();
        }

        var currentSession = await sessionRepository.GetByIdAsync(
            sessionId,
            cancellationToken);
        if (currentSession is null
            || IsTerminal(currentSession.Status)
            || !completeStop
            && currentSession.Status == CollectorSessionStatus.Stopping)
        {
            return UnitResult.Success<Error>();
        }

        return UnitResult.Failure(
            CollectorSessionShutdownErrors.StateTransitionConflict(sessionId));
    }

    private async Task<UnitResult<Error>> FailSessionAsync(
        CollectorSessionId sessionId,
        Error error,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaximumUpdateAttempts; attempt++)
        {
            var session = await sessionRepository.GetByIdAsync(
                sessionId,
                cancellationToken);
            if (session is null || IsTerminal(session.Status))
                return UnitResult.Success<Error>();

            var expectedStatus = session.Status;
            var lowerBound = session.StartedAt ?? session.CreatedAt;
            var currentTime = timeProvider.GetUtcNow();
            var failedAt = currentTime < lowerBound
                ? lowerBound
                : currentTime;

            var transitionResult = session.Fail(
                failedAt,
                CollectorStopReason.PersistenceFailure,
                error.Code,
                error.Message);
            if (transitionResult.IsFailure)
                return transitionResult;

            var updateResult = await sessionRepository.TryUpdateAsync(
                session,
                expectedStatus,
                cancellationToken);
            if (updateResult.IsFailure)
                return UnitResult.Failure(updateResult.Error);

            if (updateResult.Value == CollectorSessionUpdateStatus.Updated)
                return UnitResult.Success<Error>();
        }

        return UnitResult.Failure(
            CollectorSessionShutdownErrors.StateTransitionConflict(sessionId));
    }

    private static bool IsTerminal(CollectorSessionStatus status)
    {
        return status is CollectorSessionStatus.Stopped
            or CollectorSessionStatus.Failed
            or CollectorSessionStatus.Interrupted;
    }
}
