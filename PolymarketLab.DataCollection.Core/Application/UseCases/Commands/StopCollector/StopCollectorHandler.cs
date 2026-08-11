using CSharpFunctionalExtensions;
using MediatR;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.DataCollection.Core.Application.UseCases.Common;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using static PolymarketLab.SharedKernel.Errors.Error;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StopCollector;

public sealed class StopCollectorHandler(
    ICollectorSessionRepository sessionRepository,
    ICollectorSessionProgressRepository progressRepository,
    ICollectorSessionProgressCompletion progressCompletion,
    ICollectorRuntime runtime,
    TimeProvider timeProvider)
    : IRequestHandler<StopCollectorCommand, Result<StopCollectorResponse, ErrorList>>
{
    private const int MaximumUpdateAttempts = 3;

    public async Task<Result<StopCollectorResponse, ErrorList>> Handle(
        StopCollectorCommand command,
        CancellationToken cancellationToken)
    {
        var sessionIdResult = CollectorSessionId.Create(command.SessionId);
        if (sessionIdResult.IsFailure)
            return Failure(sessionIdResult.Error);

        var sessionId = sessionIdResult.Value;
        var stoppingResult = await MarkStoppingAsync(sessionId, cancellationToken);
        if (stoppingResult.IsFailure)
            return Result.Failure<StopCollectorResponse, ErrorList>(stoppingResult.Error);

        var session = stoppingResult.Value;
        if (IsTerminal(session.Status))
            return await ResponseAsync(session, cancellationToken);

        var runtimeResult = await runtime.StopAsync(session.Id, cancellationToken);
        if (runtimeResult.IsFailure)
        {
            await progressCompletion.CompleteAsync(session.Id, cancellationToken);
            var failedResult = await MarkFailedAsync(
                session.Id,
                CollectorStopReason.FatalWebSocketError,
                runtimeResult.Error,
                cancellationToken);
            if (failedResult.IsFailure)
                return Result.Failure<StopCollectorResponse, ErrorList>(failedResult.Error);

            return Failure(runtimeResult.Error);
        }

        var progressResult = await progressCompletion.CompleteAsync(
            session.Id,
            cancellationToken);
        if (progressResult.IsFailure)
        {
            var failedResult = await MarkFailedAsync(
                session.Id,
                CollectorStopReason.PersistenceFailure,
                progressResult.Error,
                cancellationToken);
            if (failedResult.IsFailure)
                return Result.Failure<StopCollectorResponse, ErrorList>(failedResult.Error);

            return Failure(progressResult.Error);
        }

        var stoppedResult = await MarkStoppedAsync(session.Id, cancellationToken);
        if (stoppedResult.IsFailure)
            return Result.Failure<StopCollectorResponse, ErrorList>(stoppedResult.Error);

        return await ResponseAsync(stoppedResult.Value, cancellationToken);
    }

    private async Task<Result<CollectorSessionAggregate, ErrorList>> MarkStoppingAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaximumUpdateAttempts; attempt++)
        {
            var session = await sessionRepository.GetByIdAsync(
                sessionId,
                cancellationToken);
            if (session is null)
                return FailureSession(StopCollectorErrors.SessionNotFound(sessionId.Value));

            if (IsTerminal(session.Status) || session.Status == CollectorSessionStatus.Stopping)
                return session;

            var expectedStatus = session.Status;
            var transitionResult = session.MarkStopping();
            if (transitionResult.IsFailure)
                return FailureSession(transitionResult.Error);

            var updateResult = await sessionRepository.TryUpdateAsync(
                session,
                expectedStatus,
                cancellationToken);
            if (updateResult.IsFailure)
                return FailureSession(updateResult.Error);

            if (updateResult.Value == CollectorSessionUpdateStatus.Updated)
                return session;
        }

        return FailureSession(StopCollectorErrors.StateTransitionConflict(sessionId));
    }

    private async Task<Result<CollectorSessionAggregate, ErrorList>> MarkStoppedAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaximumUpdateAttempts; attempt++)
        {
            var session = await sessionRepository.GetByIdAsync(
                sessionId,
                cancellationToken);
            if (session is null)
                return FailureSession(StopCollectorErrors.SessionNotFound(sessionId.Value));

            if (IsTerminal(session.Status))
                return session;

            var expectedStatus = session.Status;
            var lowerBound = session.StartedAt ?? session.CreatedAt;
            var currentTime = timeProvider.GetUtcNow();
            var stoppedAt = currentTime < lowerBound
                ? lowerBound
                : currentTime;
            var transitionResult = session.Stop(
                stoppedAt,
                CollectorStopReason.Requested);
            if (transitionResult.IsFailure)
                return FailureSession(transitionResult.Error);

            var updateResult = await sessionRepository.TryUpdateAsync(
                session,
                expectedStatus,
                cancellationToken);
            if (updateResult.IsFailure)
                return FailureSession(updateResult.Error);

            if (updateResult.Value == CollectorSessionUpdateStatus.Updated)
                return session;
        }

        return FailureSession(StopCollectorErrors.StateTransitionConflict(sessionId));
    }

    private async Task<Result<CollectorSessionAggregate, ErrorList>> MarkFailedAsync(
        CollectorSessionId sessionId,
        CollectorStopReason stopReason,
        Error error,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaximumUpdateAttempts; attempt++)
        {
            var session = await sessionRepository.GetByIdAsync(
                sessionId,
                cancellationToken);
            if (session is null)
                return FailureSession(StopCollectorErrors.SessionNotFound(sessionId.Value));

            if (IsTerminal(session.Status))
                return session;

            var expectedStatus = session.Status;
            var lowerBound = session.StartedAt ?? session.CreatedAt;
            var currentTime = timeProvider.GetUtcNow();
            var failedAt = currentTime < lowerBound ? lowerBound : currentTime;
            var transitionResult = session.Fail(
                failedAt,
                stopReason,
                error.Code,
                error.Message);
            if (transitionResult.IsFailure)
                return FailureSession(transitionResult.Error);

            var updateResult = await sessionRepository.TryUpdateAsync(
                session,
                expectedStatus,
                cancellationToken);
            if (updateResult.IsFailure)
                return FailureSession(updateResult.Error);

            if (updateResult.Value == CollectorSessionUpdateStatus.Updated)
                return session;
        }

        return FailureSession(StopCollectorErrors.StateTransitionConflict(sessionId));
    }

    private static bool IsTerminal(CollectorSessionStatus status)
    {
        return status is CollectorSessionStatus.Stopped
            or CollectorSessionStatus.Failed
            or CollectorSessionStatus.Interrupted;
    }

    private async Task<StopCollectorResponse> ResponseAsync(
        CollectorSessionAggregate session,
        CancellationToken cancellationToken)
    {
        var progress = await progressRepository.GetAsync(session.Id, cancellationToken);
        return new StopCollectorResponse(
            CollectorSessionResponse.FromSession(session, progress));
    }

    private static Result<StopCollectorResponse, ErrorList> Failure(params Error[] errors)
    {
        return Result.Failure<StopCollectorResponse, ErrorList>(errors.ToList());
    }

    private static Result<CollectorSessionAggregate, ErrorList> FailureSession(
        params Error[] errors)
    {
        return Result.Failure<CollectorSessionAggregate, ErrorList>(errors.ToList());
    }
}
