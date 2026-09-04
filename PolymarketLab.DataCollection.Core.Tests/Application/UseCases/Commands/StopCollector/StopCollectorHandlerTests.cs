using CSharpFunctionalExtensions;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StopCollector;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionInvalidation;
using PolymarketLab.DataCollection.Core.Application.UseCases.Common;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.DataCollection.Core.Tests.TestSupport;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Tests.Application.UseCases.Commands.StopCollector;

public sealed class StopCollectorHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_WithMissingSession_ShouldReturnNotFound()
    {
        var fixture = new Fixture();
        fixture.Repository.Sessions.Enqueue(null);

        var result = await fixture.HandleAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Code.Should().Be("collector.stop.session.not_found");
        fixture.Runtime.StopCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithStoppedSession_ShouldReturnCurrentState()
    {
        var fixture = new Fixture();
        var session = CreateRunningSession(fixture.SessionId);
        session.Stop(Now, CollectorStopReason.Requested);
        fixture.Repository.Sessions.Enqueue(session);

        var result = await fixture.HandleAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Session.Status.Should().Be("Stopped");
        result.Value.Session.SessionId.Should().Be(session.Id.Value);
        fixture.Repository.UpdateCalls.Should().BeEmpty();
        fixture.Runtime.StopCallCount.Should().Be(0);
    }

    [Theory]
    [InlineData(CollectorSessionStatus.Failed)]
    [InlineData(CollectorSessionStatus.Interrupted)]
    public async Task Handle_WithTerminalSession_ShouldReturnCurrentState(
        CollectorSessionStatus status)
    {
        var fixture = new Fixture();
        var session = CreateRunningSession(fixture.SessionId);
        if (status == CollectorSessionStatus.Failed)
        {
            session.Fail(
                Now,
                CollectorStopReason.FatalWebSocketError,
                "collector.runtime.receive.failed",
                "Receive failed.");
        }
        else
        {
            session.Interrupt(Now, CollectorStopReason.RecoveryTimeout);
        }

        fixture.Repository.Sessions.Enqueue(session);

        var result = await fixture.HandleAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Session.Status.Should().Be(status.ToString());
        result.Value.Session.FailureCode.Should().Be(session.FailureCode);
        result.Value.Session.FailureMessage.Should().Be(session.FailureMessage);
        fixture.Repository.UpdateCalls.Should().BeEmpty();
        fixture.Runtime.StopCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithStoppingSession_ShouldStopRuntimeAndRemainInvalidating()
    {
        var fixture = new Fixture();
        var session = CreateRunningSession(fixture.SessionId);
        session.MarkStopping();
        fixture.Repository.Sessions.Enqueue(session);
        fixture.Repository.Sessions.Enqueue(CloneRunningSession(session, CollectorSessionStatus.Stopping));

        var result = await fixture.HandleAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Session.Status.Should().Be("Invalidating");
        fixture.Runtime.StopCallCount.Should().Be(1);
        fixture.Repository.UpdateCalls.Should().ContainSingle();
        fixture.Repository.UpdateCalls[0].ExpectedStatus.Should().Be(CollectorSessionStatus.Stopping);
        fixture.Repository.UpdateCalls[0].StopReason.Should().Be(CollectorStopReason.Requested);
    }

    [Fact]
    public async Task Handle_WithRunningSession_ShouldInvalidateAndStopRuntime()
    {
        var fixture = new Fixture();
        fixture.ProgressRepository.Progress = fixture.ProgressRepository.Progress with
        {
            MessagesReceived = 8,
            MessagesPersisted = 8,
            LastMessageAt = Now.AddSeconds(-1),
            ReconnectCount = 1
        };
        var session = CreateRunningSession(fixture.SessionId);
        fixture.Repository.Sessions.Enqueue(session);
        fixture.Repository.Sessions.Enqueue(CloneRunningSession(session, CollectorSessionStatus.Stopping));

        var result = await fixture.HandleAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Session.Status.Should().Be("Invalidating");
        fixture.Runtime.StopCallCount.Should().Be(1);
        fixture.Runtime.StoppedSessionId.Should().Be(fixture.SessionId);
        fixture.Repository.UpdateCalls.Should().ContainSingle();
        fixture.Repository.UpdateCalls[0].Status.Should().Be(CollectorSessionStatus.Invalidating);
        fixture.Repository.UpdateCalls[0].ExpectedStatus.Should().Be(CollectorSessionStatus.Running);
        fixture.Repository.UpdateCalls[0].StopReason.Should().Be(CollectorStopReason.Requested);
        fixture.ProgressCompletion.CallCount.Should().Be(0);
        result.Value.Session.MessagesReceived.Should().Be(8);
        result.Value.Session.MessagesPersisted.Should().Be(8);
        result.Value.Session.LastMessageAt.Should().Be(Now.AddSeconds(-1));
        result.Value.Session.ReconnectCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ShouldNotWaitForSuccessfulProgressCompletionAfterFence()
    {
        var progressError = new Error(
            "collector.progress.persistence_failed",
            "Collector session progress could not be persisted.",
            ErrorType.Failure);
        var fixture = new Fixture();
        fixture.ProgressCompletion.Result = UnitResult.Failure(progressError);
        var session = CreateRunningSession(fixture.SessionId);
        fixture.Repository.Sessions.Enqueue(session);
        fixture.Repository.Sessions.Enqueue(
            CloneRunningSession(session, CollectorSessionStatus.Stopping));

        var result = await fixture.HandleAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Session.Status.Should().Be("Invalidating");
        fixture.ProgressCompletion.CallCount.Should().Be(0);
        fixture.Repository.UpdateCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_WhenRuntimeStopFails_ShouldKeepSessionInvalidatingAndReturnRuntimeError()
    {
        var runtimeError = new Error(
            "collector.runtime.stop.failed",
            "Stop failed.",
            ErrorType.Failure);
        var fixture = new Fixture();
        fixture.Runtime.StopResult = UnitResult.Failure(runtimeError);
        var session = CreateRunningSession(fixture.SessionId);
        fixture.Repository.Sessions.Enqueue(session);
        fixture.Repository.Sessions.Enqueue(
            CloneRunningSession(session, CollectorSessionStatus.Stopping));

        var result = await fixture.HandleAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Should().Be(runtimeError);
        fixture.Runtime.StopCallCount.Should().Be(1);
        fixture.Repository.UpdateCalls.Should().ContainSingle();
        fixture.Repository.UpdateCalls[0].Status.Should().Be(CollectorSessionStatus.Invalidating);
        fixture.Repository.UpdateCalls[0].StopReason.Should().Be(CollectorStopReason.Requested);
    }

    [Fact]
    public async Task Handle_WhenStoppedUpdatePersistsTerminalFailure_ShouldReturnFailureState()
    {
        var fixture = new Fixture();
        var session = CreateRunningSession(fixture.SessionId);
        var failed = CreateRunningSession(fixture.SessionId);
        failed.Fail(
            Now,
            CollectorStopReason.FatalWebSocketError,
            "collector.runtime.receive.failed",
            "Receive failed.");
        fixture.Repository.Sessions.Enqueue(session);
        fixture.Repository.Sessions.Enqueue(failed);
        fixture.Repository.UpdateResults.Enqueue(
            CollectorSessionUpdateStatus.ConcurrencyConflict);

        var result = await fixture.HandleAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Session.Status.Should().Be("Failed");
        result.Value.Session.FailureCode.Should().Be("collector.runtime.receive.failed");
        fixture.Runtime.StopCallCount.Should().Be(0);
        fixture.Repository.UpdateCalls.Should().ContainSingle();
        fixture.Repository.UpdateCalls[0].Status.Should().Be(CollectorSessionStatus.Invalidating);
    }

    [Fact]
    public async Task Handle_WhenStoppedUpdateFails_ShouldReturnPersistenceError()
    {
        var persistenceError = new Error(
            "collector.session.update.failed",
            "Session update failed.",
            ErrorType.Failure);
        var fixture = new Fixture();
        var session = CreateRunningSession(fixture.SessionId);
        fixture.Repository.Sessions.Enqueue(session);
        fixture.Repository.UpdateResults.Enqueue(
            Result.Failure<CollectorSessionUpdateStatus, Error>(persistenceError));

        var result = await fixture.HandleAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Should().Be(persistenceError);
        fixture.Runtime.StopCallCount.Should().Be(0);
        fixture.Repository.UpdateCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_WhenStoppingUpdateConflicts_ShouldReloadAndRetry()
    {
        var fixture = new Fixture();
        var first = CreateRunningSession(fixture.SessionId);
        var second = CreateRunningSession(fixture.SessionId);
        fixture.Repository.Sessions.Enqueue(first);
        fixture.Repository.Sessions.Enqueue(second);
        fixture.Repository.Sessions.Enqueue(CloneRunningSession(second, CollectorSessionStatus.Stopping));
        fixture.Repository.UpdateResults.Enqueue(CollectorSessionUpdateStatus.ConcurrencyConflict);
        fixture.Repository.UpdateResults.Enqueue(CollectorSessionUpdateStatus.Updated);
        fixture.Repository.UpdateResults.Enqueue(CollectorSessionUpdateStatus.Updated);

        var result = await fixture.HandleAsync();

        result.IsSuccess.Should().BeTrue();
        fixture.Repository.UpdateCalls.Should().HaveCount(2);
        fixture.Repository.UpdateCalls[0].ExpectedStatus.Should().Be(CollectorSessionStatus.Running);
        fixture.Repository.UpdateCalls[1].ExpectedStatus.Should().Be(CollectorSessionStatus.Running);
    }

    private static CollectorSessionAggregate CreateRunningSession(
        CollectorSessionId sessionId)
    {
        return CollectorSessionTestFactory.CreateRunning(
            sessionId,
            createdAt: Now.AddMinutes(-1),
            subscriptionReadyAt: Now.AddSeconds(-30));
    }

    private static CollectorSessionAggregate CloneRunningSession(
        CollectorSessionAggregate source,
        CollectorSessionStatus status)
    {
        var session = CollectorSessionTestFactory.CreateRunning(
            source.Id,
            source.MarketId,
            source.CreatedAt,
            source.SubscriptionReadyAt);

        if (status == CollectorSessionStatus.Stopping)
            session.MarkStopping();

        return session;
    }

    private sealed class Fixture
    {
        public CollectorSessionId SessionId { get; } =
            CollectorSessionId.Create(Guid.NewGuid()).Value;

        public StubRepository Repository { get; } = new();
        public StubRuntime Runtime { get; } = new();
        public StubProgressRepository ProgressRepository { get; } = new();
        public StubProgressCompletion ProgressCompletion { get; } = new();

        public Task<Result<StopCollectorResponse, Error.ErrorList>> HandleAsync(
            Guid? sessionId = null)
        {
            var responseFactory = new CollectorSessionResponseFactory(
                ProgressRepository,
                new StubCollectorTokenReadinessRepository(),
                new StubResolutionObservationRepository(),
                new StubCollectorDatasetCleanupAuditReader(),
                new StubNormalizationSuitabilityReader());
            var handler = new StopCollectorHandler(
                new CollectorSessionInvalidationCoordinator(Repository, Runtime),
                responseFactory,
                Runtime,
                new FixedTimeProvider(Now));

            return handler.Handle(
                new StopCollectorCommand(sessionId ?? SessionId.Value),
                CancellationToken.None);
        }
    }

    private sealed class StubProgressRepository : ICollectorSessionProgressRepository
    {
        public CollectorSessionProgress Progress { get; set; } = new(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            0,
            0,
            0,
            0,
            0,
            null,
            0);

        public Task<CollectorSessionProgress> GetAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Progress with { SessionId = sessionId });
        }

        public Task CheckpointAsync(
            CollectorSessionProgressCheckpoint checkpoint,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubProgressCompletion : ICollectorSessionProgressCompletion
    {
        public UnitResult<Error> Result { get; set; } = UnitResult.Success<Error>();
        public int CallCount { get; private set; }

        public Task<UnitResult<Error>> CompleteAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Result);
        }
    }

    private sealed class StubRepository : ICollectorSessionRepository
    {
        public Queue<CollectorSessionAggregate?> Sessions { get; } = [];
        public Queue<Result<CollectorSessionUpdateStatus, Error>> UpdateResults { get; } = [];
        public List<UpdateCall> UpdateCalls { get; } = [];
        public int GetByIdCallCount { get; private set; }

        public Task<CollectorSessionAggregate?> GetByIdAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken)
        {
            GetByIdCallCount++;
            return Task.FromResult(
                Sessions.TryDequeue(out var session) ? session : null);
        }

        public Task<CollectorSessionAggregate?> GetExclusiveAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<CollectorSessionUpdateStatus, Error>> TryUpdateAsync(
            CollectorSessionAggregate session,
            CollectorSessionStatus expectedStatus,
            CancellationToken cancellationToken)
        {
            UpdateCalls.Add(new UpdateCall(
                session.Status,
                expectedStatus,
                session.StopReason));
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

    private sealed class StubRuntime : ICollectorRuntime
    {
        public UnitResult<Error> StopResult { get; set; } = UnitResult.Success<Error>();
        public int StopCallCount { get; private set; }
        public CollectorSessionId? StoppedSessionId { get; private set; }

        public void FenceSession(CollectorSessionId sessionId)
        {
        }

        public Task<UnitResult<Error>> StopAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken)
        {
            StopCallCount++;
            StoppedSessionId = sessionId;
            return Task.FromResult(StopResult);
        }

        public Task<UnitResult<Error>> StartAsync(
            CollectorRuntimeStartRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed record UpdateCall(
        CollectorSessionStatus Status,
        CollectorSessionStatus ExpectedStatus,
        CollectorStopReason? StopReason);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
