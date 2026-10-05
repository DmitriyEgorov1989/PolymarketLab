using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.SharedKernel.Errors;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorFailedDatasetFinalization;

/// <inheritdoc />
public sealed class CollectorFailedDatasetFinalizer(
    ICollectorSessionRepository sessionRepository,
    ICollectorDatasetCleanup datasetCleanup,
    TimeProvider timeProvider) : ICollectorFailedDatasetFinalizer
{
    /// <inheritdoc />
    public async Task<UnitResult<Error>> FinalizeAsync(
        CollectorSessionAggregate session,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (session.Status == CollectorSessionStatus.Failed
            && session.DatasetDisposition == CollectorDatasetDisposition.Retained)
        {
            if (session.RetainUntil is null || now < session.RetainUntil.Value)
                return UnitResult.Success<Error>();

            return await CleanupAsync(session, cancellationToken);
        }

        if (session.Status != CollectorSessionStatus.Invalidating)
            return UnitResult.Success<Error>();

        if (session.FailurePolicy == CollectorFailurePolicy.DeleteOnFailure)
            return await CleanupAsync(session, cancellationToken);

        var completion = session.CompleteInvalidation(now);
        if (completion.IsFailure)
            return completion;

        var update = await sessionRepository.TryUpdateAsync(
            session,
            CollectorSessionStatus.Invalidating,
            cancellationToken);
        if (update.IsFailure)
            return UnitResult.Failure(update.Error);
        if (update.Value == CollectorSessionUpdateStatus.Updated)
            return UnitResult.Success<Error>();

        var current = await sessionRepository.GetByIdAsync(session.Id, cancellationToken);
        return current is not null
            && current.Status == CollectorSessionStatus.Failed
            && current.DatasetDisposition == CollectorDatasetDisposition.Retained
                ? UnitResult.Success<Error>()
                : UnitResult.Failure(
                    CollectorFailedDatasetFinalizationErrors.StateTransitionConflict(session.Id));
    }

    private async Task<UnitResult<Error>> CleanupAsync(
        CollectorSessionAggregate session,
        CancellationToken cancellationToken)
    {
        var cleanup = await datasetCleanup.CleanupAsync(session, cancellationToken);
        return cleanup.IsSuccess
            ? UnitResult.Success<Error>()
            : UnitResult.Failure(cleanup.Error);
    }
}
