using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Application.Resolution;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorRawDatasetCompletion;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionInvalidation;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Domain.Models.Resolution;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.SharedKernel.Errors;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.ResolutionConsensus;

/// <summary>Продвигает resolution lifecycle и подтверждает winner по трём устойчивым источникам.</summary>
public sealed class ResolutionConsensusCoordinator(
    ICollectorSessionRepository sessionRepository,
    ICollectorSessionProgressRepository progressRepository,
    IWebSocketResolutionCandidateSource webSocketSource,
    IResolutionObservationRepository observationRepository,
    IGammaTerminalResolutionSource gammaSource,
    IClobTerminalResolutionSource clobSource,
    ICollectorSessionInvalidationCoordinator invalidationCoordinator,
    ICollectorRuntime runtime,
    ICollectorRawDatasetCompletionCoordinator rawDatasetCompletion,
    WebSocketResolutionValidator webSocketValidator,
    TimeProvider timeProvider) : IResolutionConsensusCoordinator
{
    private const int MaximumUpdateAttempts = 3;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ConfirmationTimeout = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim _tickGate = new(1, 1);

    /// <inheritdoc />
    public async Task<UnitResult<Error>> TickAsync(CancellationToken cancellationToken)
    {
        await _tickGate.WaitAsync(cancellationToken);
        try
        {
            return await TickCoreAsync(cancellationToken);
        }
        finally
        {
            _tickGate.Release();
        }
    }

    private async Task<UnitResult<Error>> TickCoreAsync(CancellationToken cancellationToken)
    {
        var session = await sessionRepository.GetExclusiveAsync(cancellationToken);
        if (session is null || session.Status != CollectorSessionStatus.Running)
            return UnitResult.Success<Error>();

        var now = timeProvider.GetUtcNow();
        if (!HasResolutionSnapshot(session))
            return await InvalidateAsync(session, now, ResolutionErrors.Conflict, cancellationToken);

        if (now < session.EventStartsAt!.Value)
            return UnitResult.Success<Error>();

        var phaseResult = await AdvancePhaseAsync(session, now, cancellationToken);
        if (phaseResult.IsFailure)
            return UnitResult.Failure(phaseResult.Error);

        session = phaseResult.Value;
        if (session.Status != CollectorSessionStatus.Running
            || now < session.EventEndsAt!.Value
            || session.Phase != CollectorSessionPhase.AwaitingResolution)
        {
            return UnitResult.Success<Error>();
        }

        var deadline = session.EventEndsAt.Value + ConfirmationTimeout;
        var state = await observationRepository.GetStateAsync(session.Id, cancellationToken);
        if (state.Confirmation is not null)
        {
            return await rawDatasetCompletion.CompleteAsync(
                session.Id,
                cancellationToken);
        }

        now = timeProvider.GetUtcNow();
        if (state.Confirmation is null && now < deadline && ShouldPoll(state, now))
        {
            var pollingStartedAt = timeProvider.GetUtcNow();
            if (pollingStartedAt < deadline)
            {
                var pollingResult = await PollAsync(
                    session,
                    pollingStartedAt,
                    cancellationToken);
                if (pollingResult.IsFailure)
                    return UnitResult.Failure(pollingResult.Error);
                if (pollingResult.Value)
                {
                    return await InvalidateAsync(
                        session,
                        timeProvider.GetUtcNow(),
                        ResolutionErrors.Conflict,
                        cancellationToken);
                }
            }
        }

        state = await observationRepository.GetStateAsync(session.Id, cancellationToken);
        var scanResult = await ScanWebSocketAsync(
            session,
            state,
            deadline,
            cancellationToken);
        if (scanResult.IsFailure)
            return UnitResult.Failure(scanResult.Error);
        if (scanResult.Value)
        {
            return await InvalidateAsync(
                session,
                timeProvider.GetUtcNow(),
                ResolutionErrors.Conflict,
                cancellationToken);
        }

        now = timeProvider.GetUtcNow();
        state = await observationRepository.GetStateAsync(session.Id, cancellationToken);
        var consensusResult = await EvaluateConsensusAsync(
            session,
            state,
            now,
            deadline,
            cancellationToken);
        if (consensusResult.IsFailure)
            return UnitResult.Failure(consensusResult.Error);

        if (consensusResult.Value == ConsensusEvaluation.Confirmed)
        {
            return await rawDatasetCompletion.CompleteAsync(
                session.Id,
                cancellationToken);
        }

        if (consensusResult.Value == ConsensusEvaluation.Invalidated)
            return UnitResult.Success<Error>();

        return now >= deadline
            ? await InvalidateAsync(
                session,
                now,
                ResolutionErrors.ConfirmationTimeout,
                cancellationToken)
            : UnitResult.Success<Error>();
    }

    private async Task<Result<CollectorSessionAggregate, Error>> AdvancePhaseAsync(
        CollectorSessionAggregate initialSession,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var session = initialSession;
        if (session.Phase == CollectorSessionPhase.ReadyBeforeWindow)
        {
            var collectingResult = await SavePhaseTransitionAsync(
                session,
                static current => current.MarkCollectingWindow(),
                CollectorSessionPhase.CollectingWindow,
                cancellationToken);
            if (collectingResult.IsFailure)
                return collectingResult.Error;
            session = collectingResult.Value;
        }

        if (session.Status == CollectorSessionStatus.Running
            && now >= session.EventEndsAt!.Value
            && session.Phase == CollectorSessionPhase.CollectingWindow)
        {
            return await SavePhaseTransitionAsync(
                session,
                static current => current.MarkAwaitingResolution(),
                CollectorSessionPhase.AwaitingResolution,
                cancellationToken);
        }

        return session;
    }

    private async Task<Result<CollectorSessionAggregate, Error>> SavePhaseTransitionAsync(
        CollectorSessionAggregate initialSession,
        Func<CollectorSessionAggregate, UnitResult<Error>> transition,
        CollectorSessionPhase targetPhase,
        CancellationToken cancellationToken)
    {
        var session = initialSession;
        for (var attempt = 0; attempt < MaximumUpdateAttempts; attempt++)
        {
            if (session.Status != CollectorSessionStatus.Running
                || session.Phase == targetPhase)
            {
                return session;
            }

            var transitionResult = transition(session);
            if (transitionResult.IsFailure)
                return transitionResult.Error;

            var update = await sessionRepository.TryUpdateAsync(
                session,
                CollectorSessionStatus.Running,
                cancellationToken);
            if (update.IsFailure)
                return update.Error;
            if (update.Value == CollectorSessionUpdateStatus.Updated)
                return session;

            var current = await sessionRepository.GetByIdAsync(session.Id, cancellationToken);
            if (current is null)
                return StateTransitionConflict(session.Id.Value);
            session = current;
        }

        return StateTransitionConflict(session.Id.Value);
    }

    private async Task<Result<bool, Error>> ScanWebSocketAsync(
        CollectorSessionAggregate session,
        DurableResolutionState state,
        DateTimeOffset confirmationDeadline,
        CancellationToken cancellationToken)
    {
        var progress = await progressRepository.GetAsync(session.Id, cancellationToken);
        var scan = await webSocketSource.ScanAsync(
            session.Id,
            state.LastScannedRawMessageId,
            cancellationToken);
        var validations = new List<DurableWebSocketResolutionValidation>(scan.Candidates.Count);
        var conflict = false;

        foreach (var candidate in scan.Candidates)
        {
            var validation = webSocketValidator.Validate(
                candidate,
                session,
                progress.CurrentConnectionEpoch,
                confirmationDeadline);
            if (validation.IsFailure)
            {
                conflict = true;
                validations.Add(new DurableWebSocketResolutionValidation(
                    candidate,
                    DurableResolutionObservationStatus.Conflict,
                    null,
                    validation.Error.Code,
                    validation.Error.Message));
                continue;
            }

            var value = validation.Value;
            validations.Add(new DurableWebSocketResolutionValidation(
                candidate,
                value.Status == WebSocketResolutionObservationStatus.Terminal
                    ? DurableResolutionObservationStatus.Terminal
                    : DurableResolutionObservationStatus.Rejected,
                value.Winner,
                value.RejectionCode,
                value.RejectionCode));
        }

        await observationRepository.SaveWebSocketScanAsync(
            new DurableWebSocketResolutionScan(
                session.Id,
                scan.LastScannedRawMessageId,
                validations),
            cancellationToken);
        return conflict;
    }

    private static bool ShouldPoll(DurableResolutionState state, DateTimeOffset now) =>
        state.LastPollingCycleAt is null
        || now >= state.LastPollingCycleAt.Value + PollingInterval;

    private async Task<Result<bool, Error>> PollAsync(
        CollectorSessionAggregate session,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        await observationRepository.RecordPollingCycleAsync(
            session.Id,
            startedAt,
            cancellationToken);

        var gammaTask = gammaSource.GetAsync(CreateGammaRequest(session), cancellationToken);
        var clobTask = clobSource.GetAsync(CreateClobRequest(session), cancellationToken);
        await Task.WhenAll(gammaTask, clobTask);

        var gammaResult = await gammaTask;
        var clobResult = await clobTask;
        var immediateConflict = false;

        if (gammaResult.IsSuccess)
        {
            await observationRepository.SaveGammaObservationAsync(
                session.Id,
                gammaResult.Value,
                cancellationToken);
            immediateConflict |= IsMalformed(gammaResult.Value);
        }
        else
        {
            await SaveFailureAsync(
                session,
                ResolutionObservationSource.Gamma,
                gammaResult.Error,
                cancellationToken);
            immediateConflict |= !IsTransientAdapterError(gammaResult.Error);
        }

        if (clobResult.IsSuccess)
        {
            await observationRepository.SaveClobObservationAsync(
                session.Id,
                clobResult.Value,
                cancellationToken);
            immediateConflict |= IsMalformed(clobResult.Value);
        }
        else
        {
            await SaveFailureAsync(
                session,
                ResolutionObservationSource.Clob,
                clobResult.Error,
                cancellationToken);
            immediateConflict |= !IsTransientAdapterError(clobResult.Error);
        }

        return immediateConflict;
    }

    private async Task SaveFailureAsync(
        CollectorSessionAggregate session,
        ResolutionObservationSource source,
        Error error,
        CancellationToken cancellationToken)
    {
        await observationRepository.SaveFailureAsync(
            new DurableResolutionFailure(
                session.Id,
                source,
                timeProvider.GetUtcNow(),
                error.Code,
                error.Message),
            cancellationToken);
    }

    private async Task<Result<ConsensusEvaluation, Error>> EvaluateConsensusAsync(
        CollectorSessionAggregate session,
        DurableResolutionState state,
        DateTimeOffset now,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        var terminal = state.Observations
            .Where(observation =>
                observation.Status == DurableResolutionObservationStatus.Terminal
                && observation.ObservedAt <= deadline)
            .ToArray();
        if (terminal.Any(observation => observation.Winner is null))
        {
            var invalidation = await InvalidateAsync(
                session,
                now,
                ResolutionErrors.Conflict,
                cancellationToken);
            return invalidation.IsFailure
                ? invalidation.Error
                : ConsensusEvaluation.Invalidated;
        }

        var winners = terminal
            .Select(observation => observation.Winner!)
            .Distinct()
            .ToArray();
        if (winners.Length > 1)
        {
            var invalidation = await InvalidateAsync(
                session,
                now,
                ResolutionErrors.Conflict,
                cancellationToken);
            return invalidation.IsFailure
                ? invalidation.Error
                : ConsensusEvaluation.Invalidated;
        }

        if (state.Confirmation is not null)
            return ConsensusEvaluation.Confirmed;

        var webSocket = terminal.FirstOrDefault(observation =>
            observation.Source == ResolutionObservationSource.WebSocket);
        var gamma = terminal.LastOrDefault(observation =>
            observation.Source == ResolutionObservationSource.Gamma);
        var clob = terminal.LastOrDefault(observation =>
            observation.Source == ResolutionObservationSource.Clob);
        if (webSocket is null || gamma is null || clob is null || winners.Length != 1)
            return ConsensusEvaluation.Pending;

        var confirmedAt = new[]
        {
            webSocket.ObservedAt,
            gamma.ObservedAt,
            clob.ObservedAt
        }.Max();
        var confirmationResult = await ConfirmAsync(
            session,
            webSocket,
            winners[0],
            confirmedAt,
            cancellationToken);
        if (confirmationResult.IsFailure)
            return confirmationResult.Error;
        if (!confirmationResult.Value)
            return ConsensusEvaluation.Pending;

        await observationRepository.SetConfirmationReferenceAsync(
            session.Id,
            new ResolutionConfirmationReference(gamma.Id, clob.Id, confirmedAt),
            cancellationToken);
        return ConsensusEvaluation.Confirmed;
    }

    private async Task<Result<bool, Error>> ConfirmAsync(
        CollectorSessionAggregate initialSession,
        DurableResolutionObservation webSocket,
        ResolutionWinner winner,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var session = initialSession;
        for (var attempt = 0; attempt < MaximumUpdateAttempts; attempt++)
        {
            if (HasConfirmedWinner(session, winner))
                return true;
            if (session.Status != CollectorSessionStatus.Running
                || session.Phase != CollectorSessionPhase.AwaitingResolution
                || webSocket.ConnectionEpoch is null)
            {
                return false;
            }

            var confirmation = session.ConfirmResolution(
                webSocket.ObservedAt,
                now,
                winner,
                webSocket.ConnectionEpoch.Value);
            if (confirmation.IsFailure)
                return confirmation.Error;

            var update = await sessionRepository.TryUpdateAsync(
                session,
                CollectorSessionStatus.Running,
                cancellationToken);
            if (update.IsFailure)
                return update.Error;
            if (update.Value == CollectorSessionUpdateStatus.Updated)
                return true;

            var current = await sessionRepository.GetByIdAsync(session.Id, cancellationToken);
            if (current is null)
                return StateTransitionConflict(session.Id.Value);
            session = current;
        }

        return StateTransitionConflict(session.Id.Value);
    }

    private async Task<UnitResult<Error>> InvalidateAsync(
        CollectorSessionAggregate session,
        DateTimeOffset occurredAt,
        Error failure,
        CancellationToken cancellationToken)
    {
        var result = await invalidationCoordinator.InvalidateAsync(
            session.Id,
            occurredAt,
            CollectorStopReason.ResolutionFailure,
            failure,
            cancellationToken);
        if (result.IsFailure)
            return UnitResult.Failure(result.Error);

        return await runtime.StopAsync(session.Id, cancellationToken);
    }

    private static bool HasResolutionSnapshot(CollectorSessionAggregate session) =>
        session.EventStartsAt is not null
        && session.EventEndsAt is not null
        && !string.IsNullOrWhiteSpace(session.ExternalEventId)
        && !string.IsNullOrWhiteSpace(session.EventSlug)
        && !string.IsNullOrWhiteSpace(session.ExternalMarketId)
        && !string.IsNullOrWhiteSpace(session.MarketSlug)
        && !string.IsNullOrWhiteSpace(session.ConditionId)
        && session.Tokens.Count > 0;

    private static GammaTerminalResolutionRequest CreateGammaRequest(
        CollectorSessionAggregate session) => new(
        session.ExternalEventId!,
        session.EventSlug!,
        session.ExternalMarketId!,
        session.MarketSlug!,
        session.ConditionId!,
        session.Tokens.Select(token => new GammaResolutionTokenIdentity(
            token.TokenId.Value,
            token.Outcome,
            token.OutcomeIndex)).ToArray());

    private static ClobTerminalResolutionRequest CreateClobRequest(
        CollectorSessionAggregate session) => new(
        session.ConditionId!,
        session.Tokens.Select(token => new ClobResolutionTokenIdentity(
            token.TokenId.Value,
            token.Outcome,
            token.OutcomeIndex)).ToArray());

    private static bool IsTransientAdapterError(Error error) =>
        error.Type == ErrorType.Failure
        && (error.Code.EndsWith(".timeout", StringComparison.Ordinal)
            || error.Code.EndsWith(".network", StringComparison.Ordinal)
            || error.Code.EndsWith(".http_error", StringComparison.Ordinal));

    private static bool IsMalformed(GammaTerminalResolutionObservation observation) =>
        observation.Status == GammaTerminalResolutionStatus.Terminal
            ? observation.Winner is null || !HasWinnerIdentity(observation.Winner.TokenId, observation.Winner.Outcome)
            : observation.Winner is not null;

    private static bool IsMalformed(ClobTerminalResolutionObservation observation) =>
        observation.Status == ClobTerminalResolutionStatus.Terminal
            ? observation.Winner is null || !HasWinnerIdentity(observation.Winner.TokenId, observation.Winner.Outcome)
            : observation.Winner is not null;

    private static bool HasWinnerIdentity(string tokenId, string outcome) =>
        !string.IsNullOrWhiteSpace(tokenId) && !string.IsNullOrWhiteSpace(outcome);

    private static bool HasConfirmedWinner(
        CollectorSessionAggregate session,
        ResolutionWinner winner) =>
        session.ResolutionConfirmedAt is not null
        && string.Equals(session.WinningTokenId, winner.TokenId, StringComparison.Ordinal)
        && string.Equals(session.WinningOutcome, winner.Outcome, StringComparison.Ordinal);

    private static Error StateTransitionConflict(Guid sessionId) => new(
        "collector.resolution.state_transition_conflict",
        $"Collector session '{sessionId}' changed concurrently during resolution consensus.",
        ErrorType.Conflict);

    private enum ConsensusEvaluation
    {
        Pending,
        Confirmed,
        Invalidated
    }
}
