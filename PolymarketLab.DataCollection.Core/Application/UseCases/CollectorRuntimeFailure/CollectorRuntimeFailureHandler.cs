using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.SharedKernel.Errors;
using CollectorRuntimeFailureNotification = PolymarketLab.DataCollection.Core.Ports.Dtos.CollectorRuntimeFailure;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorRuntimeFailure;

public sealed class CollectorRuntimeFailureHandler(
    ICollectorSessionRepository sessionRepository)
    : ICollectorRuntimeFailureHandler
{
    private const int MaximumUpdateAttempts = 2;

    public async Task<UnitResult<Error>> HandleAsync(
        CollectorRuntimeFailureNotification failure,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaximumUpdateAttempts; attempt++)
        {
            var session = await sessionRepository.GetByIdAsync(
                failure.SessionId,
                cancellationToken);

            if (session is null || session.Status is
                CollectorSessionStatus.Stopping or
                CollectorSessionStatus.Stopped or
                CollectorSessionStatus.Failed or
                CollectorSessionStatus.Interrupted)
            {
                return UnitResult.Success<Error>();
            }

            var expectedStatus = session.Status;
            var earliestFailureTime = session.StartedAt ?? session.CreatedAt;
            var failedAt = failure.FailedAt < earliestFailureTime
                ? earliestFailureTime
                : failure.FailedAt;

            var failResult = session.Fail(
                failedAt,
                CollectorStopReason.FatalWebSocketError,
                failure.Error.Code,
                failure.Error.Message);
            if (failResult.IsFailure)
                return failResult;

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
            CollectorRuntimeFailureErrors.StateTransitionConflict(
                failure.SessionId));
    }
}
