using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionInvalidation;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.SharedKernel.Errors;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionStartupReconciliation;

public sealed class CollectorSessionStartupReconciler(
    ICollectorSessionRepository sessionRepository,
    ICollectorSessionInvalidationCoordinator invalidationCoordinator,
    ICollectorDatasetCleanup datasetCleanup,
    TimeProvider timeProvider)
    : ICollectorSessionStartupReconciler
{
    public async Task<UnitResult<Error>> ReconcileAsync(
        CancellationToken cancellationToken)
    {
        var activeSessions = await sessionRepository.GetActiveAsync(cancellationToken);

        foreach (var session in activeSessions)
        {
            var result = await invalidationCoordinator.InvalidateAsync(
                session.Id,
                timeProvider.GetUtcNow(),
                CollectorStopReason.ProcessTerminated,
                CollectorSessionStartupReconciliationErrors.ProcessTerminated,
                cancellationToken);
            if (result.IsFailure)
                return UnitResult.Failure(result.Error);
            if (result.Value is null || result.Value.Status != CollectorSessionStatus.Invalidating)
                continue;

            var cleanup = await datasetCleanup.CleanupAsync(
                result.Value,
                cancellationToken);
            if (cleanup.IsFailure)
                return UnitResult.Failure(cleanup.Error);
        }

        return UnitResult.Success<Error>();
    }
}
