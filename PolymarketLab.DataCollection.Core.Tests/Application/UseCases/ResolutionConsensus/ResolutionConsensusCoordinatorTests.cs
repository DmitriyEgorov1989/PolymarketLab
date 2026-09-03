using CSharpFunctionalExtensions;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.Resolution;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorRawDatasetCompletion;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionInvalidation;
using PolymarketLab.DataCollection.Core.Application.UseCases.ResolutionConsensus;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Domain.Models.Resolution;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.DataCollection.Core.Tests.TestSupport;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Tests.Application.UseCases.ResolutionConsensus;

public sealed class ResolutionConsensusCoordinatorTests
{
    [Fact]
    public async Task TickAsync_AtExactEventStart_ShouldEnterCollectingWindowWithoutScanning()
    {
        var fixture = new Fixture();
        fixture.Time.SetUtcNow(fixture.Session.EventStartsAt!.Value);

        var result = await fixture.Coordinator.TickAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Session.Phase.Should().Be(CollectorSessionPhase.CollectingWindow);
        fixture.WebSocket.CallCount.Should().Be(0);
        fixture.Gamma.CallCount.Should().Be(0);
        fixture.Clob.CallCount.Should().Be(0);
        fixture.Sessions.ExpectedStatuses.Should().Equal(CollectorSessionStatus.Running);
    }

    [Fact]
    public async Task TickAsync_AtExactEventEndWithoutWebSocket_ShouldStartPolling()
    {
        var fixture = new Fixture();

        var result = await fixture.Coordinator.TickAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Session.Phase.Should().Be(CollectorSessionPhase.AwaitingResolution);
        fixture.Gamma.CallCount.Should().Be(1);
        fixture.Clob.CallCount.Should().Be(1);
        fixture.Observations.LastPollingCycleAt.Should().Be(fixture.EventEndsAt);
    }

    [Fact]
    public async Task TickAsync_BeforeTwoSecondsSinceLastCycle_ShouldNotPollAgain()
    {
        var fixture = new Fixture();
        await fixture.Coordinator.TickAsync(CancellationToken.None);
        fixture.Time.SetUtcNow(fixture.EventEndsAt.AddMilliseconds(1999));

        var result = await fixture.Coordinator.TickAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Gamma.CallCount.Should().Be(1);
        fixture.Clob.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task TickAsync_WithNonTerminalAndTransientFailure_ShouldPersistAndRetry()
    {
        var fixture = new Fixture();
        fixture.Gamma.Results.Enqueue(Result.Success<GammaTerminalResolutionObservation, Error>(
            fixture.CreateGamma(GammaTerminalResolutionStatus.NonTerminal)));
        fixture.Clob.Results.Enqueue(Result.Failure<ClobTerminalResolutionObservation, Error>(
            new Error(
                "clob.terminal_resolution.timeout",
                "The CLOB terminal resolution request timed out.",
                ErrorType.Failure)));

        await fixture.Coordinator.TickAsync(CancellationToken.None);
        fixture.Time.SetUtcNow(fixture.EventEndsAt.AddSeconds(2));
        var result = await fixture.Coordinator.TickAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Gamma.CallCount.Should().Be(2);
        fixture.Clob.CallCount.Should().Be(2);
        fixture.Observations.Observations.Should().Contain(observation =>
            observation.Status == DurableResolutionObservationStatus.NonTerminal);
        fixture.Observations.Observations.Should().Contain(observation =>
            observation.Status == DurableResolutionObservationStatus.Failed
            && observation.ErrorCode == "clob.terminal_resolution.timeout");
        fixture.Invalidation.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task TickAsync_WhenTerminalSourcesDisagree_ShouldInvalidateImmediately()
    {
        var fixture = new Fixture();
        fixture.Gamma.Results.Enqueue(Result.Success<GammaTerminalResolutionObservation, Error>(
            fixture.CreateGamma(GammaTerminalResolutionStatus.Terminal, winnerIndex: 0)));
        fixture.Clob.Results.Enqueue(Result.Success<ClobTerminalResolutionObservation, Error>(
            fixture.CreateClob(ClobTerminalResolutionStatus.Terminal, winnerIndex: 1)));

        var result = await fixture.Coordinator.TickAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Invalidation.Calls.Should().ContainSingle();
        fixture.Invalidation.Calls[0].Reason.Should().Be(CollectorStopReason.ResolutionFailure);
        fixture.Invalidation.Calls[0].Failure.Code.Should().Be(ResolutionErrors.Conflict.Code);
        fixture.Runtime.StopCalls.Should().Equal(fixture.Session.Id);
        fixture.RawCompletion.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task TickAsync_WhenWebSocketCandidateConflictsWithSnapshot_ShouldPersistAndInvalidate()
    {
        var fixture = new Fixture();
        var candidate = fixture.CreateWebSocketCandidate() with
        {
            ConditionId = "0xdifferent"
        };
        fixture.WebSocket.Scans.Enqueue(new WebSocketResolutionScanResult(9, [candidate]));

        var result = await fixture.Coordinator.TickAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Observations.Observations.Should().ContainSingle(observation =>
            observation.Source == ResolutionObservationSource.WebSocket
            && observation.Status == DurableResolutionObservationStatus.Conflict);
        fixture.Invalidation.Calls.Should().ContainSingle();
        fixture.Invalidation.Calls[0].Failure.Code.Should().Be(ResolutionErrors.Conflict.Code);
        fixture.Gamma.CallCount.Should().Be(1);
        fixture.Clob.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task TickAsync_WithThreeSourceConsensus_ShouldPersistSessionAndReference()
    {
        var fixture = new Fixture();
        fixture.WebSocket.Scans.Enqueue(new WebSocketResolutionScanResult(
            42,
            [fixture.CreateWebSocketCandidate()]));
        fixture.Gamma.Results.Enqueue(Result.Success<GammaTerminalResolutionObservation, Error>(
            fixture.CreateGamma(GammaTerminalResolutionStatus.Terminal, winnerIndex: 0)));
        fixture.Clob.Results.Enqueue(Result.Success<ClobTerminalResolutionObservation, Error>(
            fixture.CreateClob(ClobTerminalResolutionStatus.Terminal, winnerIndex: 0)));

        var result = await fixture.Coordinator.TickAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Session.ResolutionSignaledAt.Should().Be(fixture.EventEndsAt);
        fixture.Session.ResolutionConfirmedAt.Should().Be(fixture.EventEndsAt);
        fixture.Session.WinningTokenId.Should().Be("1001");
        fixture.Session.WinningOutcome.Should().Be("Yes");
        fixture.Session.ResolutionConnectionEpoch.Should().Be(2);
        fixture.Session.Status.Should().Be(CollectorSessionStatus.Running);
        fixture.Session.Phase.Should().Be(CollectorSessionPhase.AwaitingResolution);
        fixture.Observations.Confirmation.Should().NotBeNull();

        var gamma = fixture.Observations.Observations.Single(observation =>
            observation.Source == ResolutionObservationSource.Gamma);
        var clob = fixture.Observations.Observations.Single(observation =>
            observation.Source == ResolutionObservationSource.Clob);
        fixture.Observations.Confirmation!.PrimaryObservationId.Should().Be(gamma.Id);
        fixture.Observations.Confirmation.ConfirmingObservationId.Should().Be(clob.Id);
        fixture.Invalidation.Calls.Should().BeEmpty();
        fixture.RawCompletion.Calls.Should().Equal(fixture.Session.Id);
        fixture.Calls.Should().Equal("confirmation_reference", "raw_completion");

        fixture.Time.SetUtcNow(fixture.EventEndsAt.AddSeconds(2));
        await fixture.Coordinator.TickAsync(CancellationToken.None);
        fixture.WebSocket.CallCount.Should().Be(1);
        fixture.Gamma.CallCount.Should().Be(1);
        fixture.Clob.CallCount.Should().Be(1);
        fixture.RawCompletion.Calls.Should().Equal(fixture.Session.Id, fixture.Session.Id);
        fixture.Calls.Should().Equal(
            "confirmation_reference",
            "raw_completion",
            "raw_completion");
    }

    [Fact]
    public async Task TickAsync_WithPersistedConfirmation_ShouldCompleteWithoutPollingAgain()
    {
        var fixture = new Fixture();
        fixture.Observations.Confirmation = new ResolutionConfirmationReference(
            1,
            2,
            fixture.EventEndsAt);

        var result = await fixture.Coordinator.TickAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.WebSocket.CallCount.Should().Be(0);
        fixture.Gamma.CallCount.Should().Be(0);
        fixture.Clob.CallCount.Should().Be(0);
        fixture.RawCompletion.Calls.Should().Equal(fixture.Session.Id);
        fixture.Invalidation.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task TickAsync_AfterConsensusWithDifferentWebSocketCandidate_ShouldCompleteWithoutScanningAgain()
    {
        var fixture = new Fixture();
        fixture.WebSocket.Scans.Enqueue(new WebSocketResolutionScanResult(
            42,
            [fixture.CreateWebSocketCandidate()]));
        fixture.Gamma.Results.Enqueue(Result.Success<GammaTerminalResolutionObservation, Error>(
            fixture.CreateGamma(GammaTerminalResolutionStatus.Terminal, winnerIndex: 0)));
        fixture.Clob.Results.Enqueue(Result.Success<ClobTerminalResolutionObservation, Error>(
            fixture.CreateClob(ClobTerminalResolutionStatus.Terminal, winnerIndex: 0)));
        await fixture.Coordinator.TickAsync(CancellationToken.None);
        fixture.Time.SetUtcNow(fixture.EventEndsAt.AddSeconds(2));
        fixture.WebSocket.Scans.Enqueue(new WebSocketResolutionScanResult(
            43,
            [fixture.CreateWebSocketCandidate() with
            {
                RawMessageId = 43,
                WinningAssetId = "1002",
                WinningOutcome = "No"
            }]));

        var result = await fixture.Coordinator.TickAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.WebSocket.CallCount.Should().Be(1);
        fixture.RawCompletion.Calls.Should().Equal(fixture.Session.Id, fixture.Session.Id);
        fixture.Invalidation.Calls.Should().BeEmpty();
        fixture.Runtime.StopCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task TickAsync_AtConfirmationDeadlineWithoutConsensus_ShouldTimeoutWithoutPolling()
    {
        var fixture = new Fixture(TimeSpan.FromMinutes(5));

        var result = await fixture.Coordinator.TickAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Gamma.CallCount.Should().Be(0);
        fixture.Clob.CallCount.Should().Be(0);
        fixture.Invalidation.Calls.Should().ContainSingle();
        fixture.Invalidation.Calls[0].Failure.Code.Should()
            .Be(ResolutionErrors.ConfirmationTimeout.Code);
        fixture.Runtime.StopCalls.Should().Equal(fixture.Session.Id);
    }

    [Fact]
    public async Task TickAsync_WhenPollingCycleCrossesDeadline_ShouldTimeoutWithoutConsensus()
    {
        var fixture = new Fixture(TimeSpan.FromMinutes(5).Subtract(TimeSpan.FromSeconds(1)));
        var gammaCompletion = new TaskCompletionSource<
            Result<GammaTerminalResolutionObservation, Error>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var clobCompletion = new TaskCompletionSource<
            Result<ClobTerminalResolutionObservation, Error>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Gamma.PendingResult = gammaCompletion;
        fixture.Clob.PendingResult = clobCompletion;
        fixture.WebSocket.Scans.Enqueue(new WebSocketResolutionScanResult(
            42,
            [fixture.CreateWebSocketCandidate()]));

        var tick = fixture.Coordinator.TickAsync(CancellationToken.None);
        fixture.Time.SetUtcNow(fixture.EventEndsAt.AddMinutes(5).AddMilliseconds(1));
        gammaCompletion.SetResult(Result.Success<GammaTerminalResolutionObservation, Error>(
            fixture.CreateGamma(GammaTerminalResolutionStatus.Terminal)));
        clobCompletion.SetResult(Result.Success<ClobTerminalResolutionObservation, Error>(
            fixture.CreateClob(ClobTerminalResolutionStatus.Terminal)));
        var result = await tick;

        result.IsSuccess.Should().BeTrue();
        fixture.Session.ResolutionConfirmedAt.Should().BeNull();
        fixture.Invalidation.Calls.Should().ContainSingle(call =>
            call.Failure.Code == ResolutionErrors.ConfirmationTimeout.Code);
    }

    [Fact]
    public async Task TickAsync_WithConcurrentTicks_ShouldNotOverlapPollingCycles()
    {
        var fixture = new Fixture();
        var gammaCompletion = new TaskCompletionSource<
            Result<GammaTerminalResolutionObservation, Error>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var clobCompletion = new TaskCompletionSource<
            Result<ClobTerminalResolutionObservation, Error>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Gamma.PendingResult = gammaCompletion;
        fixture.Clob.PendingResult = clobCompletion;

        var firstTick = fixture.Coordinator.TickAsync(CancellationToken.None);
        var secondTick = fixture.Coordinator.TickAsync(CancellationToken.None);

        fixture.Gamma.CallCount.Should().Be(1);
        fixture.Clob.CallCount.Should().Be(1);
        secondTick.IsCompleted.Should().BeFalse();

        gammaCompletion.SetResult(Result.Success<GammaTerminalResolutionObservation, Error>(
            fixture.CreateGamma(GammaTerminalResolutionStatus.NonTerminal)));
        clobCompletion.SetResult(Result.Success<ClobTerminalResolutionObservation, Error>(
            fixture.CreateClob(ClobTerminalResolutionStatus.NonTerminal)));
        await Task.WhenAll(firstTick, secondTick);

        fixture.Gamma.CallCount.Should().Be(1);
        fixture.Clob.CallCount.Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TickAsync_WithPreEndOrStaleWebSocketCandidate_ShouldNotReachConsensus(
        bool preEnd)
    {
        var fixture = new Fixture();
        fixture.WebSocket.Scans.Enqueue(new WebSocketResolutionScanResult(
            7,
            [fixture.CreateWebSocketCandidate(
                receivedAt: preEnd ? fixture.EventEndsAt.AddTicks(-1) : fixture.EventEndsAt,
                connectionEpoch: preEnd ? 2 : 1)]));
        fixture.Gamma.Results.Enqueue(Result.Success<GammaTerminalResolutionObservation, Error>(
            fixture.CreateGamma(GammaTerminalResolutionStatus.Terminal, winnerIndex: 0)));
        fixture.Clob.Results.Enqueue(Result.Success<ClobTerminalResolutionObservation, Error>(
            fixture.CreateClob(ClobTerminalResolutionStatus.Terminal, winnerIndex: 0)));

        var result = await fixture.Coordinator.TickAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Session.ResolutionConfirmedAt.Should().BeNull();
        fixture.Observations.Confirmation.Should().BeNull();
        fixture.Observations.Observations.Should().Contain(observation =>
            observation.Source == ResolutionObservationSource.WebSocket
            && observation.Status == DurableResolutionObservationStatus.Rejected);
        fixture.Invalidation.Calls.Should().BeEmpty();
    }

    private sealed class Fixture
    {
        private static readonly DateTimeOffset CreatedAt =
            DateTimeOffset.Parse("2026-09-01T10:00:00Z");

        public Fixture(TimeSpan? afterEventEnd = null)
        {
            Session = CollectorSessionTestFactory.CreateRunning(createdAt: CreatedAt);
            EventEndsAt = Session.EventEndsAt!.Value;
            Time = new MutableTimeProvider(EventEndsAt + (afterEventEnd ?? TimeSpan.Zero));
            Calls = [];
            Sessions = new SessionRepository(Session);
            Progress = new ProgressRepository(Session.Id, currentConnectionEpoch: 2);
            WebSocket = new WebSocketSource();
            Observations = new ObservationRepository(Session.Id, Calls);
            Gamma = new GammaSource(() => CreateGamma(GammaTerminalResolutionStatus.NonTerminal));
            Clob = new ClobSource(() => CreateClob(ClobTerminalResolutionStatus.NonTerminal));
            Invalidation = new InvalidationCoordinator(Session);
            Runtime = new CollectorRuntime();
            RawCompletion = new RawCompletionCoordinator(Calls);
            Coordinator = new ResolutionConsensusCoordinator(
                Sessions,
                Progress,
                WebSocket,
                Observations,
                Gamma,
                Clob,
                Invalidation,
                Runtime,
                RawCompletion,
                new WebSocketResolutionValidator(),
                Time);
        }

        public CollectorSessionAggregate Session { get; }
        public DateTimeOffset EventEndsAt { get; }
        public MutableTimeProvider Time { get; }
        public List<string> Calls { get; }
        public SessionRepository Sessions { get; }
        public ProgressRepository Progress { get; }
        public WebSocketSource WebSocket { get; }
        public ObservationRepository Observations { get; }
        public GammaSource Gamma { get; }
        public ClobSource Clob { get; }
        public InvalidationCoordinator Invalidation { get; }
        public CollectorRuntime Runtime { get; }
        public RawCompletionCoordinator RawCompletion { get; }
        public IResolutionConsensusCoordinator Coordinator { get; }

        public WebSocketResolutionCandidate CreateWebSocketCandidate(
            DateTimeOffset? receivedAt = null,
            long connectionEpoch = 2) => new(
            1,
            0,
            connectionEpoch,
            receivedAt ?? EventEndsAt,
            Session.ExternalMarketId,
            Session.ConditionId,
            Session.Tokens.Select(token => token.TokenId.Value).ToArray(),
            "1001",
            "Yes");

        public GammaTerminalResolutionObservation CreateGamma(
            GammaTerminalResolutionStatus status,
            int winnerIndex = 0)
        {
            var outcomes = CreateGammaOutcomes(winnerIndex);
            return new GammaTerminalResolutionObservation(
                Time.GetUtcNow(),
                Session.ExternalEventId!,
                Session.EventSlug!,
                Session.ExternalMarketId!,
                Session.MarketSlug!,
                Session.ConditionId!,
                status == GammaTerminalResolutionStatus.Terminal,
                status != GammaTerminalResolutionStatus.Terminal,
                status == GammaTerminalResolutionStatus.Terminal ? "resolved" : null,
                null,
                status,
                outcomes,
                status == GammaTerminalResolutionStatus.Terminal ? outcomes[winnerIndex] : null);
        }

        public ClobTerminalResolutionObservation CreateClob(
            ClobTerminalResolutionStatus status,
            int winnerIndex = 0)
        {
            var outcomes = CreateClobOutcomes(winnerIndex);
            return new ClobTerminalResolutionObservation(
                Time.GetUtcNow(),
                Session.ConditionId!,
                status == ClobTerminalResolutionStatus.Terminal,
                status != ClobTerminalResolutionStatus.Terminal,
                status,
                outcomes,
                status == ClobTerminalResolutionStatus.Terminal ? outcomes[winnerIndex] : null);
        }

        private static GammaResolutionOutcome[] CreateGammaOutcomes(int winnerIndex) =>
        [
            new("1001", "Yes", 0, winnerIndex == 0 ? 1m : 0m),
            new("1002", "No", 1, winnerIndex == 1 ? 1m : 0m)
        ];

        private static ClobResolutionOutcome[] CreateClobOutcomes(int winnerIndex) =>
        [
            new("1001", "Yes", 0, winnerIndex == 0 ? 1m : 0m),
            new("1002", "No", 1, winnerIndex == 1 ? 1m : 0m)
        ];
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
    }

    private sealed class SessionRepository(CollectorSessionAggregate session)
        : ICollectorSessionRepository
    {
        public List<CollectorSessionStatus> ExpectedStatuses { get; } = [];

        public Task<CollectorSessionAggregate?> GetByIdAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken) => Task.FromResult<CollectorSessionAggregate?>(session);

        public Task<CollectorSessionAggregate?> GetExclusiveAsync(CancellationToken cancellationToken) =>
            Task.FromResult<CollectorSessionAggregate?>(session);

        public Task<CollectorSessionAggregate?> GetActiveByMarketIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken) => Task.FromResult<CollectorSessionAggregate?>(session);

        public Task<CollectorSessionAggregate?> GetCurrentByMarketIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken) => Task.FromResult<CollectorSessionAggregate?>(session);

        public Task<IReadOnlyCollection<CollectorSessionAggregate>> GetActiveAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<CollectorSessionAggregate>>([session]);

        public Task<Result<CollectorSessionInsertStatus, Error>> TryAddAsync(
            CollectorSessionAggregate value,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<CollectorSessionUpdateStatus, Error>> TryUpdateAsync(
            CollectorSessionAggregate value,
            CollectorSessionStatus expectedStatus,
            CancellationToken cancellationToken)
        {
            ExpectedStatuses.Add(expectedStatus);
            return Task.FromResult(Result.Success<CollectorSessionUpdateStatus, Error>(
                CollectorSessionUpdateStatus.Updated));
        }
    }

    private sealed class ProgressRepository(CollectorSessionId sessionId, long currentConnectionEpoch)
        : ICollectorSessionProgressRepository
    {
        public Task<CollectorSessionProgress> GetAsync(
            CollectorSessionId requestedSessionId,
            CancellationToken cancellationToken) => Task.FromResult(new CollectorSessionProgress(
                sessionId,
                currentConnectionEpoch,
                0,
                0,
                0,
                0,
                null,
                0));

        public Task CheckpointAsync(
            CollectorSessionProgressCheckpoint checkpoint,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class WebSocketSource : IWebSocketResolutionCandidateSource
    {
        public Queue<WebSocketResolutionScanResult> Scans { get; } = [];
        public int CallCount { get; private set; }

        public Task<WebSocketResolutionScanResult> ScanAsync(
            CollectorSessionId sessionId,
            long afterRawMessageId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(
                Scans.TryDequeue(out var scan)
                    ? scan
                    : new WebSocketResolutionScanResult(afterRawMessageId, []));
        }
    }

    private sealed class GammaSource(Func<GammaTerminalResolutionObservation> defaultResult)
        : IGammaTerminalResolutionSource
    {
        public Queue<Result<GammaTerminalResolutionObservation, Error>> Results { get; } = [];
        public int CallCount { get; private set; }
        public TaskCompletionSource<Result<GammaTerminalResolutionObservation, Error>>? PendingResult { get; set; }

        public Task<Result<GammaTerminalResolutionObservation, Error>> GetAsync(
            GammaTerminalResolutionRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (PendingResult is not null)
                return PendingResult.Task;
            return Task.FromResult(Results.TryDequeue(out var result)
                ? result
                : Result.Success<GammaTerminalResolutionObservation, Error>(defaultResult()));
        }
    }

    private sealed class ClobSource(Func<ClobTerminalResolutionObservation> defaultResult)
        : IClobTerminalResolutionSource
    {
        public Queue<Result<ClobTerminalResolutionObservation, Error>> Results { get; } = [];
        public int CallCount { get; private set; }
        public TaskCompletionSource<Result<ClobTerminalResolutionObservation, Error>>? PendingResult { get; set; }

        public Task<Result<ClobTerminalResolutionObservation, Error>> GetAsync(
            ClobTerminalResolutionRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (PendingResult is not null)
                return PendingResult.Task;
            return Task.FromResult(Results.TryDequeue(out var result)
                ? result
                : Result.Success<ClobTerminalResolutionObservation, Error>(defaultResult()));
        }
    }

    private sealed class RawCompletionCoordinator(List<string> calls)
        : ICollectorRawDatasetCompletionCoordinator
    {
        public List<CollectorSessionId> Calls { get; } = [];

        public Task<UnitResult<Error>> CompleteAsync(
            CollectorSessionId requestedSessionId,
            CancellationToken cancellationToken)
        {
            Calls.Add(requestedSessionId);
            calls.Add("raw_completion");
            return Task.FromResult(UnitResult.Success<Error>());
        }
    }

    private sealed class InvalidationCoordinator(CollectorSessionAggregate session)
        : ICollectorSessionInvalidationCoordinator
    {
        public List<InvalidationCall> Calls { get; } = [];

        public Task<Result<CollectorSessionAggregate?, Error>> InvalidateAsync(
            CollectorSessionId sessionId,
            DateTimeOffset occurredAt,
            CollectorStopReason reason,
            Error failure,
            CancellationToken cancellationToken)
        {
            Calls.Add(new InvalidationCall(occurredAt, reason, failure));
            return Task.FromResult(Result.Success<CollectorSessionAggregate?, Error>(session));
        }
    }

    private sealed record InvalidationCall(
        DateTimeOffset OccurredAt,
        CollectorStopReason Reason,
        Error Failure);

    private sealed class CollectorRuntime : ICollectorRuntime
    {
        public List<CollectorSessionId> StopCalls { get; } = [];

        public void FenceSession(CollectorSessionId sessionId)
        {
        }

        public Task<UnitResult<Error>> StartAsync(
            CollectorRuntimeStartRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UnitResult<Error>> StopAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken)
        {
            StopCalls.Add(sessionId);
            return Task.FromResult(UnitResult.Success<Error>());
        }
    }

    private sealed class ObservationRepository(
        CollectorSessionId sessionId,
        List<string> calls)
        : IResolutionObservationRepository
    {
        private readonly HashSet<(long RawMessageId, int RawItemIndex)> _webSocketKeys = [];
        private long _nextId = 1;

        public long LastScannedRawMessageId { get; private set; }
        public DateTimeOffset? LastPollingCycleAt { get; private set; }
        public ResolutionConfirmationReference? Confirmation { get; set; }
        public List<DurableResolutionObservation> Observations { get; } = [];

        public Task<DurableResolutionState> GetStateAsync(
            CollectorSessionId requestedSessionId,
            CancellationToken cancellationToken) => Task.FromResult(new DurableResolutionState(
                sessionId,
                LastScannedRawMessageId,
                LastPollingCycleAt,
                Confirmation,
                Observations.ToArray()));

        public Task SaveWebSocketScanAsync(
            DurableWebSocketResolutionScan scan,
            CancellationToken cancellationToken)
        {
            LastScannedRawMessageId = Math.Max(LastScannedRawMessageId, scan.LastScannedRawMessageId);
            foreach (var validation in scan.Validations)
            {
                var candidate = validation.Candidate;
                if (!_webSocketKeys.Add((candidate.RawMessageId, candidate.RawItemIndex)))
                    continue;

                Observations.Add(new DurableResolutionObservation(
                    _nextId++,
                    ResolutionObservationSource.WebSocket,
                    candidate.ReceivedAt,
                    validation.Status,
                    validation.Winner,
                    null,
                    null,
                    candidate.ExternalMarketId,
                    null,
                    candidate.ConditionId,
                    null,
                    null,
                    null,
                    null,
                    validation.ErrorCode,
                    validation.ErrorMessage,
                    candidate.RawMessageId,
                    candidate.RawItemIndex,
                    candidate.ConnectionEpoch,
                    []));
            }

            return Task.CompletedTask;
        }

        public Task<long> SaveGammaObservationAsync(
            CollectorSessionId requestedSessionId,
            GammaTerminalResolutionObservation observation,
            CancellationToken cancellationToken)
        {
            var id = _nextId++;
            Observations.Add(new DurableResolutionObservation(
                id,
                ResolutionObservationSource.Gamma,
                observation.ObservedAt,
                observation.Status == GammaTerminalResolutionStatus.Terminal
                    ? DurableResolutionObservationStatus.Terminal
                    : DurableResolutionObservationStatus.NonTerminal,
                observation.Winner is null
                    ? null
                    : new ResolutionWinner(observation.Winner.TokenId, observation.Winner.Outcome),
                observation.ExternalEventId,
                observation.EventSlug,
                observation.ExternalMarketId,
                observation.MarketSlug,
                observation.ConditionId,
                observation.Closed,
                observation.AcceptingOrders,
                observation.UmaResolutionStatus,
                observation.ExternalClosedAt,
                null,
                null,
                null,
                null,
                null,
                []));
            return Task.FromResult(id);
        }

        public Task<long> SaveClobObservationAsync(
            CollectorSessionId requestedSessionId,
            ClobTerminalResolutionObservation observation,
            CancellationToken cancellationToken)
        {
            var id = _nextId++;
            Observations.Add(new DurableResolutionObservation(
                id,
                ResolutionObservationSource.Clob,
                observation.ObservedAt,
                observation.Status == ClobTerminalResolutionStatus.Terminal
                    ? DurableResolutionObservationStatus.Terminal
                    : DurableResolutionObservationStatus.NonTerminal,
                observation.Winner is null
                    ? null
                    : new ResolutionWinner(observation.Winner.TokenId, observation.Winner.Outcome),
                null,
                null,
                null,
                null,
                observation.ConditionId,
                observation.Closed,
                observation.AcceptingOrders,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                []));
            return Task.FromResult(id);
        }

        public Task<long> SaveFailureAsync(
            DurableResolutionFailure failure,
            CancellationToken cancellationToken)
        {
            var id = _nextId++;
            Observations.Add(new DurableResolutionObservation(
                id,
                failure.Source,
                failure.ObservedAt,
                DurableResolutionObservationStatus.Failed,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                failure.ErrorCode,
                failure.ErrorMessage,
                null,
                null,
                null,
                []));
            return Task.FromResult(id);
        }

        public Task RecordPollingCycleAsync(
            CollectorSessionId requestedSessionId,
            DateTimeOffset startedAt,
            CancellationToken cancellationToken)
        {
            LastPollingCycleAt = startedAt;
            return Task.CompletedTask;
        }

        public Task SetConfirmationReferenceAsync(
            CollectorSessionId requestedSessionId,
            ResolutionConfirmationReference confirmation,
            CancellationToken cancellationToken)
        {
            Confirmation = confirmation;
            calls.Add("confirmation_reference");
            return Task.CompletedTask;
        }
    }
}
