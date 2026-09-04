using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionInvalidation;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorRuntimeReadiness;

/// <inheritdoc />
public sealed class CollectorRuntimeReadinessHandler(
    ICollectorSessionRepository sessionRepository,
    ICollectorTokenReadinessRepository tokenReadinessRepository,
    ICollectorSessionInvalidationCoordinator invalidationCoordinator,
    TimeProvider timeProvider)
    : ICollectorRuntimeReadinessHandler
{
    private const int MaximumUpdateAttempts = 2;

    public Task<UnitResult<Error>> MarkAwaitingInitialBooksAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken) =>
        UpdateStartingPhaseAsync(
            sessionId,
            session => session.MarkAwaitingInitialBooks(),
            cancellationToken);

    public Task<UnitResult<Error>> MarkAwaitingHeartbeatAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken) =>
        UpdateStartingPhaseAsync(
            sessionId,
            session => session.MarkAwaitingHeartbeat(),
            cancellationToken);

    public Task<UnitResult<Error>> MarkRunningAsync(
        CollectorSessionId sessionId,
        DateTimeOffset subscriptionReadyAt,
        CancellationToken cancellationToken) =>
        UpdateStartingPhaseAsync(
            sessionId,
            session => session.MarkRunning(subscriptionReadyAt),
            cancellationToken);

    public async Task<UnitResult<Error>> RecordInitialBookEnqueuedAsync(
        CollectorSessionId sessionId,
        TokenId tokenId,
        long connectionEpoch,
        DateTimeOffset enqueuedAt,
        CancellationToken cancellationToken)
    {
        if (connectionEpoch <= 0 || enqueuedAt == default)
            return UnitResult.Failure(CollectorRuntimeReadinessErrors.InvalidObservation(sessionId));

        var session = await sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        if (session is null
            || session.Status != CollectorSessionStatus.Starting
            || session.Phase != CollectorSessionPhase.AwaitingInitialBooks)
        {
            return UnitResult.Success<Error>();
        }

        if (!session.Tokens.Any(token => token.TokenId == tokenId))
            return UnitResult.Failure(CollectorRuntimeReadinessErrors.UnknownSnapshotToken(sessionId, tokenId));

        await tokenReadinessRepository.RecordInitialBookEnqueuedAsync(
            new CollectorTokenReadiness(sessionId, connectionEpoch, tokenId, enqueuedAt),
            cancellationToken);
        return UnitResult.Success<Error>();
    }

    public async Task<UnitResult<Error>> BeginInvalidationAsync(
        CollectorSessionId sessionId,
        Error failure,
        CancellationToken cancellationToken)
    {
        var result = await invalidationCoordinator.InvalidateAsync(
            sessionId,
            timeProvider.GetUtcNow(),
            CollectorStopReason.FatalWebSocketError,
            failure,
            cancellationToken);
        return result.IsFailure
            ? UnitResult.Failure(result.Error)
            : UnitResult.Success<Error>();
    }

    private async Task<UnitResult<Error>> UpdateStartingPhaseAsync(
        CollectorSessionId sessionId,
        Func<Domain.Models.CollectorSession.CollectorSession, UnitResult<Error>> transition,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaximumUpdateAttempts; attempt++)
        {
            var session = await sessionRepository.GetByIdAsync(
                sessionId,
                cancellationToken);

            if (session is null || session.Status != CollectorSessionStatus.Starting)
                return UnitResult.Success<Error>();

            var result = transition(session);
            if (result.IsFailure)
                return result;

            var update = await sessionRepository.TryUpdateAsync(
                session,
                CollectorSessionStatus.Starting,
                cancellationToken);
            if (update.IsFailure)
                return UnitResult.Failure(update.Error);
            if (update.Value == CollectorSessionUpdateStatus.Updated)
                return UnitResult.Success<Error>();
        }

        return UnitResult.Failure(
            CollectorRuntimeFailureErrors.StateTransitionConflict(sessionId));
    }
}
