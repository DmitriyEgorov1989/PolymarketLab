using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionInvalidation;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.Errors;
using CollectorRuntimeFailureNotification = PolymarketLab.DataCollection.Core.Ports.Dtos.CollectorRuntimeFailure;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorRuntimeFailure;

public sealed class CollectorRuntimeFailureHandler(
    ICollectorSessionInvalidationCoordinator invalidationCoordinator)
    : ICollectorRuntimeFailureHandler
{
    public async Task<UnitResult<Error>> HandleAsync(
        CollectorRuntimeFailureNotification failure,
        CancellationToken cancellationToken)
    {
        var result = await invalidationCoordinator.InvalidateAsync(
            failure.SessionId,
            failure.FailedAt,
            CollectorStopReason.FatalWebSocketError,
            failure.Error,
            cancellationToken);
        return result.IsFailure
            ? UnitResult.Failure(result.Error)
            : UnitResult.Success<Error>();
    }
}
