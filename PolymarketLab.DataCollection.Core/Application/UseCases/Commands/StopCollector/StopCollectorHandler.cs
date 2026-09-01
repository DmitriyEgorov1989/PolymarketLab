using CSharpFunctionalExtensions;
using MediatR;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionInvalidation;
using PolymarketLab.DataCollection.Core.Application.UseCases.Common;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using static PolymarketLab.SharedKernel.Errors.Error;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StopCollector;

public sealed class StopCollectorHandler(
    ICollectorSessionInvalidationCoordinator invalidationCoordinator,
    ICollectorSessionProgressRepository progressRepository,
    ICollectorRuntime runtime,
    TimeProvider timeProvider)
    : IRequestHandler<StopCollectorCommand, Result<StopCollectorResponse, ErrorList>>
{
    public async Task<Result<StopCollectorResponse, ErrorList>> Handle(
        StopCollectorCommand command,
        CancellationToken cancellationToken)
    {
        var sessionIdResult = CollectorSessionId.Create(command.SessionId);
        if (sessionIdResult.IsFailure)
            return Failure(sessionIdResult.Error);

        var sessionId = sessionIdResult.Value;
        var invalidation = await invalidationCoordinator.InvalidateAsync(
            sessionId,
            timeProvider.GetUtcNow(),
            CollectorStopReason.Requested,
            StopCollectorErrors.RequestedBeforeSuccess,
            cancellationToken);
        if (invalidation.IsFailure)
            return Failure(invalidation.Error);
        if (invalidation.Value is null)
            return Failure(StopCollectorErrors.SessionNotFound(sessionId.Value));

        var session = invalidation.Value;
        if (!IsTerminal(session.Status))
        {
            var runtimeResult = await runtime.StopAsync(session.Id, cancellationToken);
            if (runtimeResult.IsFailure)
                return Failure(runtimeResult.Error);
        }

        return await ResponseAsync(session, cancellationToken);
    }

    private static bool IsTerminal(CollectorSessionStatus status) => status is
        CollectorSessionStatus.Stopped
        or CollectorSessionStatus.Failed
        or CollectorSessionStatus.Interrupted;

    private async Task<StopCollectorResponse> ResponseAsync(
        CollectorSessionAggregate session,
        CancellationToken cancellationToken)
    {
        var progress = await progressRepository.GetAsync(session.Id, cancellationToken);
        return new StopCollectorResponse(
            CollectorSessionResponse.FromSession(session, progress));
    }

    private static Result<StopCollectorResponse, ErrorList> Failure(params Error[] errors) =>
        Result.Failure<StopCollectorResponse, ErrorList>(errors.ToList());
}
