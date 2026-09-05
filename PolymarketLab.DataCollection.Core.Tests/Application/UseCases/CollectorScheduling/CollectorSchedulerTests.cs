using CSharpFunctionalExtensions;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorScheduling;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionInvalidation;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.DataCollection.Core.Tests.TestSupport;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Tests.Application.UseCases.CollectorScheduling;

public sealed class CollectorSchedulerTests
{
    private static readonly DateTimeOffset CreatedAt =
        DateTimeOffset.Parse("2026-08-30T11:57:00Z");

    [Fact]
    public async Task TickAsync_WhenInvalidating_ShouldWaitForStopThenCleanWithoutRestart()
    {
        var fixture = new Fixture(CreatedAt.AddMinutes(2));
        fixture.Session.BeginInvalidation(
            CreatedAt.AddMinutes(1), CollectorStopReason.Requested,
            "collector.stop.requested", "Stop requested.");
        var stopped = new TaskCompletionSource<UnitResult<Error>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Runtime.StopResult = stopped.Task;

        var tick = fixture.Scheduler.TickAsync(CancellationToken.None);

        fixture.Runtime.FencedSessions.Should().Equal(fixture.Session.Id);
        fixture.Runtime.StoppedSessions.Should().Equal(fixture.Session.Id);
        fixture.Cleanup.Calls.Should().BeEmpty();
        tick.IsCompleted.Should().BeFalse();
        stopped.SetResult(UnitResult.Success<Error>());

        (await tick).IsSuccess.Should().BeTrue();
        fixture.Session.Status.Should().Be(CollectorSessionStatus.Failed);
        fixture.Session.InvalidatingAt.Should().Be(CreatedAt.AddMinutes(1));
        fixture.Session.FailureCode.Should().Be("collector.stop.requested");
        fixture.Cleanup.Calls.Should().Equal(fixture.Session.Id);
        fixture.Source.FreshReadCallCount.Should().Be(0);
        fixture.Runtime.StartRequests.Should().BeEmpty();
        (await fixture.Scheduler.TickAsync(CancellationToken.None)).IsSuccess.Should().BeTrue();
        fixture.Cleanup.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task TickAsync_WhenInvalidatingStopFails_ShouldNotCleanAndShouldRetry()
    {
        var fixture = new Fixture(CreatedAt.AddMinutes(2));
        fixture.Session.BeginInvalidation(
            CreatedAt.AddMinutes(1), CollectorStopReason.Requested,
            "collector.stop.requested", "Stop requested.");
        var error = new Error("collector.runtime.stop.timeout", "Stop timed out.", ErrorType.Failure);
        fixture.Runtime.StopResult = Task.FromResult(UnitResult.Failure(error));

        var result = await fixture.Scheduler.TickAsync(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
        fixture.Cleanup.Calls.Should().BeEmpty();
        fixture.Session.Status.Should().Be(CollectorSessionStatus.Invalidating);

        fixture.Runtime.StopResult = Task.FromResult(UnitResult.Success<Error>());
        (await fixture.Scheduler.TickAsync(CancellationToken.None)).IsSuccess.Should().BeTrue();
        fixture.Session.Status.Should().Be(CollectorSessionStatus.Failed);
    }

    [Fact]
    public async Task TickAsync_WhenCleanupFails_ShouldPreserveInvalidatingAndReturnError()
    {
        var error = new Error("collector.dataset_cleanup.failed", "Cleanup failed.", ErrorType.Failure);
        var fixture = new Fixture(CreatedAt.AddMinutes(2), cleanupError: error);
        fixture.Session.BeginInvalidation(
            CreatedAt.AddMinutes(1), CollectorStopReason.Requested,
            "collector.stop.requested", "Stop requested.");

        var result = await fixture.Scheduler.TickAsync(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
        fixture.Cleanup.Calls.Should().Equal(fixture.Session.Id);
        fixture.Session.Status.Should().Be(CollectorSessionStatus.Invalidating);
    }

    [Fact]
    public async Task TickAsync_BeforePreparationBoundary_ShouldRemainScheduledWithoutGamma()
    {
        var fixture = new Fixture(CreatedAt.AddMinutes(1));

        var result = await fixture.Scheduler.TickAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Session.Status.Should().Be(CollectorSessionStatus.Scheduled);
        fixture.Source.FreshReadCallCount.Should().Be(0);
        fixture.Repository.UpdateCalls.Should().BeEmpty();
        fixture.Runtime.StartRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task TickAsync_AtPreparationBoundary_ShouldStartWithRegularDeadline()
    {
        var fixture = new Fixture(CreatedAt.AddMinutes(2));

        var result = await fixture.Scheduler.TickAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Session.Status.Should().Be(CollectorSessionStatus.Starting);
        fixture.Session.Phase.Should().Be(CollectorSessionPhase.Connecting);
        fixture.Session.StartedAt.Should().Be(CreatedAt.AddMinutes(2));
        fixture.Repository.UpdateCalls.Should().ContainSingle()
            .Which.ExpectedStatus.Should().Be(CollectorSessionStatus.Scheduled);
        fixture.Runtime.StartRequests.Should().ContainSingle();
        fixture.Runtime.StartRequests.Single().ReadinessDeadline.Should()
            .Be(fixture.Session.EventStartsAt!.Value.AddSeconds(-10));
    }

    [Fact]
    public async Task PrepareAsync_AtLateBoundary_ShouldUseMarketOpenAsDeadline()
    {
        var fixture = new Fixture(CreatedAt.AddMinutes(2).AddSeconds(50));

        var result = await fixture.Scheduler.PrepareAsync(
            fixture.Session,
            fixture.Market,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Runtime.StartRequests.Single().ReadinessDeadline.Should()
            .Be(fixture.Session.EventStartsAt);
    }

    [Theory]
    [InlineData(false, false, true, true)]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, false)]
    public async Task PrepareAsync_WithUnavailableBoundaryFlags_ShouldInvalidate(
        bool active,
        bool closed,
        bool acceptingOrders,
        bool orderBookEnabled)
    {
        var fixture = new Fixture(CreatedAt.AddMinutes(2));
        var market = fixture.Market with
        {
            Active = active,
            Closed = closed,
            AcceptingOrders = acceptingOrders,
            OrderBookEnabled = orderBookEnabled
        };

        var result = await fixture.Scheduler.PrepareAsync(
            fixture.Session,
            market,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(CollectorSessionStatus.Invalidating);
        result.Value.Phase.Should().Be(CollectorSessionPhase.Cleaning);
        fixture.Runtime.StartRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_WithSnapshotMismatch_ShouldInvalidateWithoutMovingWindow()
    {
        var fixture = new Fixture(CreatedAt.AddMinutes(2));
        var originalStartsAt = fixture.Session.EventStartsAt;
        var changed = fixture.Market with
        {
            EventStartsAt = fixture.Market.EventStartsAt.AddMinutes(5)
        };

        var result = await fixture.Scheduler.PrepareAsync(
            fixture.Session,
            changed,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(CollectorSessionStatus.Invalidating);
        result.Value.EventStartsAt.Should().Be(originalStartsAt);
        fixture.Runtime.StartRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task TickAsync_WhenGammaTemporarilyFails_ShouldLeaveSessionForRetry()
    {
        var error = new Error("gamma.timeout", "Gamma timed out.", ErrorType.Failure);
        var fixture = new Fixture(CreatedAt.AddMinutes(2), sourceError: error);

        var result = await fixture.Scheduler.TickAsync(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
        fixture.Session.Status.Should().Be(CollectorSessionStatus.Scheduled);
        fixture.Repository.UpdateCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task TickAsync_AtMarketOpen_ShouldInvalidateWithoutGammaOrRuntime()
    {
        var fixture = new Fixture(CreatedAt.AddMinutes(3));

        var result = await fixture.Scheduler.TickAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Session.Status.Should().Be(CollectorSessionStatus.Invalidating);
        fixture.Source.FreshReadCallCount.Should().Be(0);
        fixture.Runtime.StartRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_WhenCasLosesToStartingSession_ShouldReturnWinnerWithoutSecondRuntime()
    {
        var fixture = new Fixture(CreatedAt.AddMinutes(2));
        var winner = CollectorSessionTestFactory.CreateStarting(
            fixture.Session.Id,
            fixture.Session.MarketId,
            CreatedAt);
        fixture.Repository.UpdateResults.Enqueue(
            CollectorSessionUpdateStatus.ConcurrencyConflict);
        fixture.Repository.ReloadedSessions.Enqueue(winner);
        fixture.Repository.ReloadedSessions.Enqueue(CollectorSessionTestFactory.CreateStarting(
            fixture.Session.Id,
            fixture.Session.MarketId,
            CreatedAt));

        var result = await fixture.Scheduler.PrepareAsync(
            fixture.Session,
            fixture.Market,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(winner);
        fixture.Runtime.StartRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task TickAsync_AtRegularReadinessDeadline_WhenStillStarting_ShouldInvalidate()
    {
        var fixture = new Fixture(CreatedAt.AddMinutes(2).AddSeconds(50));
        fixture.Session.BeginPreparation(CreatedAt.AddMinutes(2));

        var result = await fixture.Scheduler.TickAsync(CancellationToken.None);
        fixture.Session.Status.Should().Be(CollectorSessionStatus.Invalidating);
        var repeated = await fixture.Scheduler.TickAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repeated.IsSuccess.Should().BeTrue();
        fixture.Source.FreshReadCallCount.Should().Be(1);
        fixture.Session.Status.Should().Be(CollectorSessionStatus.Failed);
        fixture.Runtime.StoppedSessions.Should().Equal(fixture.Session.Id, fixture.Session.Id);
        fixture.Cleanup.Calls.Should().Equal(fixture.Session.Id);
    }

    [Fact]
    public async Task TickAsync_AtRegularReadinessDeadline_WhenRunning_ShouldRecheckGamma()
    {
        var fixture = new Fixture(CreatedAt.AddMinutes(2).AddSeconds(50));
        fixture.Session.BeginPreparation(CreatedAt.AddMinutes(2));
        CollectorSessionTestFactory.MarkRunning(
            fixture.Session,
            CreatedAt.AddMinutes(2).AddSeconds(30));

        var result = await fixture.Scheduler.TickAsync(CancellationToken.None);
        var repeated = await fixture.Scheduler.TickAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repeated.IsSuccess.Should().BeTrue();
        fixture.Source.FreshReadCallCount.Should().Be(1);
        fixture.Session.Status.Should().Be(CollectorSessionStatus.Running);
        fixture.Repository.UpdateCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_WhenInvalidationCasLosesToStarting_ShouldRetryAndStopRuntime()
    {
        var fixture = new Fixture(CreatedAt.AddMinutes(2));
        var winner = CollectorSessionTestFactory.CreateStarting(
            fixture.Session.Id,
            fixture.Session.MarketId,
            CreatedAt);
        fixture.Repository.UpdateResults.Enqueue(
            CollectorSessionUpdateStatus.ConcurrencyConflict);
        fixture.Repository.ReloadedSessions.Enqueue(winner);
        fixture.Repository.ReloadedSessions.Enqueue(CollectorSessionTestFactory.CreateStarting(
            fixture.Session.Id,
            fixture.Session.MarketId,
            CreatedAt));
        var mismatched = fixture.Market with { ConditionId = "0xdifferent" };

        var result = await fixture.Scheduler.PrepareAsync(
            fixture.Session,
            mismatched,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(CollectorSessionStatus.Invalidating);
        fixture.Repository.UpdateCalls.Should().HaveCount(2);
        fixture.Runtime.StoppedSessions.Should().Equal(fixture.Session.Id);
    }

    [Fact]
    public async Task PrepareAsync_WhenLastInvalidationConflictAlreadyReachedTarget_ShouldSucceed()
    {
        var fixture = new Fixture(CreatedAt.AddMinutes(2));
        var first = CollectorSessionTestFactory.CreateStarting(
            fixture.Session.Id,
            fixture.Session.MarketId,
            CreatedAt);
        var second = CollectorSessionTestFactory.CreateRunning(
            fixture.Session.Id,
            fixture.Session.MarketId,
            CreatedAt);
        var completed = CollectorSessionTestFactory.CreateStarting(
            fixture.Session.Id,
            fixture.Session.MarketId,
            CreatedAt);
        completed.BeginInvalidation(
            CreatedAt.AddMinutes(2),
            CollectorStopReason.StartupFailure,
            "collector.scheduler.session.invalid",
            "Collector session no longer satisfies its boundaries.");
        fixture.Repository.UpdateResults.Enqueue(CollectorSessionUpdateStatus.ConcurrencyConflict);
        fixture.Repository.UpdateResults.Enqueue(CollectorSessionUpdateStatus.ConcurrencyConflict);
        fixture.Repository.UpdateResults.Enqueue(CollectorSessionUpdateStatus.ConcurrencyConflict);
        fixture.Repository.ReloadedSessions.Enqueue(first);
        fixture.Repository.ReloadedSessions.Enqueue(second);
        fixture.Repository.ReloadedSessions.Enqueue(completed);
        var mismatched = fixture.Market with { ConditionId = "0xdifferent" };

        var result = await fixture.Scheduler.PrepareAsync(
            fixture.Session,
            mismatched,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(completed);
    }

    [Fact]
    public async Task PrepareAsync_WhenRuntimeStartFails_ShouldInvalidateAndPreserveRuntimeError()
    {
        var error = new Error("collector.runtime.start.failed", "Start failed.", ErrorType.Failure);
        var fixture = new Fixture(CreatedAt.AddMinutes(2), runtimeError: error);

        var result = await fixture.Scheduler.PrepareAsync(
            fixture.Session,
            fixture.Market,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
        fixture.Session.Status.Should().Be(CollectorSessionStatus.Invalidating);
        fixture.Runtime.StoppedSessions.Should().Equal(fixture.Session.Id);
    }

    [Fact]
    public async Task PrepareAsync_WhenRuntimeStartIsCancelled_ShouldInvalidateBeforeRethrow()
    {
        var fixture = new Fixture(
            CreatedAt.AddMinutes(2),
            runtimeThrowsCancellation: true);

        var action = () => fixture.Scheduler.PrepareAsync(
            fixture.Session,
            fixture.Market,
            CancellationToken.None);

        await action.Should().ThrowAsync<OperationCanceledException>();
        fixture.Session.Status.Should().Be(CollectorSessionStatus.Invalidating);
        fixture.Runtime.StoppedSessions.Should().Equal(fixture.Session.Id);
    }

    [Fact]
    public async Task PrepareAsync_WhenCancellationCompensationFails_ShouldReturnPersistenceError()
    {
        var persistenceError = new Error(
            "collector.session.update.failed",
            "Update failed.",
            ErrorType.Failure);
        var fixture = new Fixture(
            CreatedAt.AddMinutes(2),
            runtimeThrowsCancellation: true);
        fixture.Repository.UpdateResults.Enqueue(CollectorSessionUpdateStatus.Updated);
        fixture.Repository.UpdateResults.Enqueue(
            Result.Failure<CollectorSessionUpdateStatus, Error>(persistenceError));

        var result = await fixture.Scheduler.PrepareAsync(
            fixture.Session,
            fixture.Market,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(persistenceError);
    }

    private sealed class Fixture
    {
        public Fixture(
            DateTimeOffset now,
            Error? sourceError = null,
            Error? runtimeError = null,
            bool runtimeThrowsCancellation = false,
            Error? cleanupError = null)
        {
            Session = CollectorSessionTestFactory.CreateScheduled(createdAt: CreatedAt);
            Market = CreateMarket(Session);
            Source = new StubMarketSource(Market, sourceError);
            Repository = new StubRepository(Session);
            Runtime = new StubRuntime(runtimeError, runtimeThrowsCancellation);
            Cleanup = new StubCollectorDatasetCleanup(cleanupError);
            Scheduler = new CollectorScheduler(
                Source,
                Repository,
                Runtime,
                new CollectorSessionInvalidationCoordinator(Repository, Runtime),
                Cleanup,
                BoundaryChecks,
                new FixedTimeProvider(now));
        }

        public CollectorSessionAggregate Session { get; }
        public CollectionMarket Market { get; }
        public StubMarketSource Source { get; }
        public StubRepository Repository { get; }
        public StubRuntime Runtime { get; }
        public StubCollectorDatasetCleanup Cleanup { get; }
        public CollectorBoundaryCheckRegistry BoundaryChecks { get; } = new();
        public CollectorScheduler Scheduler { get; }
    }

    private static CollectionMarket CreateMarket(CollectorSessionAggregate session) => new(
        session.MarketId,
        session.ExternalEventId!,
        session.EventSlug!,
        session.ExternalMarketId!,
        session.MarketSlug!,
        session.ConditionId!,
        session.EventStartsAt!.Value,
        session.EventEndsAt!.Value,
        true,
        false,
        true,
        true,
        session.Tokens.Select(token => new CollectionMarketToken(
            token.TokenId,
            token.Outcome,
            token.OutcomeIndex)).ToArray());

    private sealed class StubMarketSource(CollectionMarket market, Error? error)
        : IMarketCollectionSource
    {
        public int FreshReadCallCount { get; private set; }

        public Task<CollectionMarketWindow?> GetWindowAsync(
            MarketId marketId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CollectionMarketWindow?>(
                market.MarketId == marketId
                    ? new CollectionMarketWindow(market.MarketId, market.EventStartsAt)
                    : null);

        public Task<Result<CollectionMarket?, Error>> GetByIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken)
        {
            FreshReadCallCount++;
            return error is null
                ? Task.FromResult<Result<CollectionMarket?, Error>>(
                    market.MarketId == marketId ? market : null)
                : Task.FromResult(Result.Failure<CollectionMarket?, Error>(error));
        }
    }

    private sealed class StubRepository(CollectorSessionAggregate exclusiveSession)
        : ICollectorSessionRepository
    {
        public Queue<Result<CollectorSessionUpdateStatus, Error>> UpdateResults { get; } = [];
        public Queue<CollectorSessionAggregate> ReloadedSessions { get; } = [];
        public List<UpdateCall> UpdateCalls { get; } = [];
        public CollectorSessionAggregate? ReloadedSession { get; set; }

        public Task<CollectorSessionAggregate?> GetExclusiveAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<CollectorSessionAggregate?>(exclusiveSession);

        public Task<CollectorSessionAggregate?> GetByIdAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CollectorSessionAggregate?>(
                ReloadedSessions.TryDequeue(out var session)
                    ? session
                    : ReloadedSession ?? exclusiveSession);

        public Task<Result<CollectorSessionUpdateStatus, Error>> TryUpdateAsync(
            CollectorSessionAggregate session,
            CollectorSessionStatus expectedStatus,
            CancellationToken cancellationToken)
        {
            UpdateCalls.Add(new UpdateCall(expectedStatus, session.Status));
            return Task.FromResult(
                UpdateResults.TryDequeue(out var result)
                    ? result
                    : Result.Success<CollectorSessionUpdateStatus, Error>(
                        CollectorSessionUpdateStatus.Updated));
        }

        public Task<CollectorSessionAggregate?> GetActiveByMarketIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CollectorSessionAggregate?> GetCurrentByMarketIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyCollection<CollectorSessionAggregate>> GetActiveAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<CollectorSessionInsertStatus, Error>> TryAddAsync(
            CollectorSessionAggregate session,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubRuntime(
        Error? startError,
        bool throwsCancellation) : ICollectorRuntime
    {
        public List<CollectorRuntimeStartRequest> StartRequests { get; } = [];
        public List<CollectorSessionId> StoppedSessions { get; } = [];
        public List<CollectorSessionId> FencedSessions { get; } = [];
        public Task<UnitResult<Error>> StopResult { get; set; } =
            Task.FromResult(UnitResult.Success<Error>());

        public void FenceSession(CollectorSessionId sessionId)
        {
            FencedSessions.Add(sessionId);
        }

        public Task<UnitResult<Error>> StartAsync(
            CollectorRuntimeStartRequest request,
            CancellationToken cancellationToken)
        {
            StartRequests.Add(request);
            if (throwsCancellation)
                throw new OperationCanceledException(cancellationToken);
            return Task.FromResult(
                startError is null
                    ? UnitResult.Success<Error>()
                    : UnitResult.Failure(startError));
        }

        public Task<UnitResult<Error>> StopAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken)
        {
            StoppedSessions.Add(sessionId);
            return StopResult.WaitAsync(cancellationToken);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed record UpdateCall(
        CollectorSessionStatus ExpectedStatus,
        CollectorSessionStatus CurrentStatus);
}
