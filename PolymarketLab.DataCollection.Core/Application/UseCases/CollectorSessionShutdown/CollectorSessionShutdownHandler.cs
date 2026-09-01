using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionInvalidation;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionShutdown;

public sealed class CollectorSessionShutdownHandler(
    ICollectorSessionInvalidationCoordinator invalidationCoordinator,
    TimeProvider timeProvider) : ICollectorSessionShutdownHandler
{
    public Task<UnitResult<Error>> MarkStoppingAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken) =>
        InvalidateAsync(
            sessionId,
            CollectorStopReason.ApplicationShutdown,
            CollectorSessionShutdownErrors.ApplicationShutdown,
            cancellationToken);

    public Task<UnitResult<Error>> MarkStoppedAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken) =>
        InvalidateAsync(
            sessionId,
            CollectorStopReason.ApplicationShutdown,
            CollectorSessionShutdownErrors.ApplicationShutdown,
            cancellationToken);

    public Task<UnitResult<Error>> MarkFailedAsync(
        CollectorSessionId sessionId,
        Error error,
        CancellationToken cancellationToken) =>
        InvalidateAsync(
            sessionId,
            CollectorStopReason.PersistenceFailure,
            error,
            cancellationToken);

    private async Task<UnitResult<Error>> InvalidateAsync(
        CollectorSessionId sessionId,
        CollectorStopReason reason,
        Error error,
        CancellationToken cancellationToken)
    {
        var result = await invalidationCoordinator.InvalidateAsync(
            sessionId,
            timeProvider.GetUtcNow(),
            reason,
            error,
            cancellationToken);
        return result.IsFailure
            ? UnitResult.Failure(result.Error)
            : UnitResult.Success<Error>();
    }
}
