using CSharpFunctionalExtensions;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorRuntimeFailure;
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
using RuntimeFailureNotification = PolymarketLab.DataCollection.Core.Ports.Dtos.CollectorRuntimeFailure;

namespace PolymarketLab.DataCollection.Core.Tests.Application.UseCases.CollectorRuntimeFailure;

public sealed class CollectorRuntimeFailureHandlerTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);
    private static readonly Error RuntimeError = new(
        "collector.runtime.receive.closed",
        "Remote endpoint closed the connection.",
        ErrorType.Failure);

    [Theory]
    [InlineData(CollectorSessionStatus.Running)]
    [InlineData(CollectorSessionStatus.Stopping)]
    public async Task HandleAsync_WithActiveSession_ShouldPersistInvalidatingState(
        CollectorSessionStatus status)
    {
        var session = CreateSession(status);
        var repository = new StubCollectorSessionRepository(session);
        var handler = CreateHandler(repository);
        var failedAt = CreatedAt.AddMinutes(1);

        var result = await handler.HandleAsync(
            new RuntimeFailureNotification(session.Id, failedAt, RuntimeError),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repository.UpdateCalls.Should().ContainSingle();
        var update = repository.UpdateCalls[0];
        update.ExpectedStatus.Should().Be(status);
        update.Session.Status.Should().Be(CollectorSessionStatus.Invalidating);
        update.Session.InvalidatingAt.Should().Be(failedAt);
        update.Session.StoppedAt.Should().BeNull();
        update.Session.StopReason.Should().Be(CollectorStopReason.FatalWebSocketError);
        update.Session.FailureCode.Should().Be(RuntimeError.Code);
        update.Session.FailureMessage.Should().Be(RuntimeError.Message);
    }

    [Fact]
    public async Task HandleAsync_WhenStartingUpdateConflicts_ShouldRetryRunningState()
    {
        var sessionId = CollectorSessionId.Create(Guid.NewGuid()).Value;
        var marketId = MarketId.Create(Guid.NewGuid()).Value;
        var starting = CreateSession(sessionId, marketId, CollectorSessionStatus.Starting);
        var running = CreateSession(sessionId, marketId, CollectorSessionStatus.Running);
        var repository = new StubCollectorSessionRepository(starting, running);
        repository.UpdateResults.Enqueue(CollectorSessionUpdateStatus.ConcurrencyConflict);
        repository.UpdateResults.Enqueue(CollectorSessionUpdateStatus.Updated);
        var handler = CreateHandler(repository);

        var result = await handler.HandleAsync(
            new RuntimeFailureNotification(
                sessionId,
                CreatedAt.AddMinutes(1),
                RuntimeError),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repository.UpdateCalls.Should().HaveCount(2);
        repository.UpdateCalls[0].ExpectedStatus.Should()
            .Be(CollectorSessionStatus.Starting);
        repository.UpdateCalls[1].ExpectedStatus.Should()
            .Be(CollectorSessionStatus.Running);
    }

    [Theory]
    [InlineData(CollectorSessionStatus.Stopped)]
    [InlineData(CollectorSessionStatus.Failed)]
    [InlineData(CollectorSessionStatus.Interrupted)]
    public async Task HandleAsync_WithNonApplicableState_ShouldBeIdempotent(
        CollectorSessionStatus status)
    {
        var session = CreateSession(status);
        var repository = new StubCollectorSessionRepository(session);
        var handler = CreateHandler(repository);

        var result = await handler.HandleAsync(
            new RuntimeFailureNotification(
                session.Id,
                CreatedAt.AddMinutes(1),
                RuntimeError),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repository.UpdateCalls.Should().BeEmpty();
    }

    private static CollectorSessionAggregate CreateSession(
        CollectorSessionStatus status)
    {
        return CreateSession(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            MarketId.Create(Guid.NewGuid()).Value,
            status);
    }

    private static CollectorSessionAggregate CreateSession(
        CollectorSessionId sessionId,
        MarketId marketId,
        CollectorSessionStatus status)
    {
        var session = CollectorSessionTestFactory.CreateScheduled(
            sessionId,
            marketId,
            CreatedAt);

        if (status == CollectorSessionStatus.Starting)
            session.BeginPreparation(CreatedAt.AddSeconds(1));
        else
            CollectorSessionTestFactory.MarkRunning(session, CreatedAt.AddSeconds(1));

        if (status == CollectorSessionStatus.Stopping)
            session.MarkStopping();
        else if (status == CollectorSessionStatus.Stopped)
            session.Stop(CreatedAt.AddSeconds(2), CollectorStopReason.Requested);
        else if (status == CollectorSessionStatus.Failed)
            session.Fail(
                CreatedAt.AddSeconds(2),
                CollectorStopReason.FatalWebSocketError,
                RuntimeError.Code,
                RuntimeError.Message);
        else if (status == CollectorSessionStatus.Interrupted)
            session.Interrupt(CreatedAt.AddSeconds(2), CollectorStopReason.ProcessTerminated);

        return session;
    }

    private static CollectorRuntimeFailureHandler CreateHandler(
        ICollectorSessionRepository repository) => new(
            new CollectorSessionInvalidationCoordinator(
                repository,
                new StubRuntime()));

    private sealed class StubCollectorSessionRepository(
        params CollectorSessionAggregate[] sessions)
        : ICollectorSessionRepository
    {
        private readonly Queue<CollectorSessionAggregate> _sessions = new(sessions);

        public Queue<Result<CollectorSessionUpdateStatus, Error>> UpdateResults { get; } = [];
        public List<UpdateCall> UpdateCalls { get; } = [];

        public Task<CollectorSessionAggregate?> GetByIdAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<CollectorSessionAggregate?>(
                _sessions.TryDequeue(out var session) ? session : null);
        }

        public Task<CollectorSessionAggregate?> GetExclusiveAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<CollectorSessionUpdateStatus, Error>> TryUpdateAsync(
            CollectorSessionAggregate session,
            CollectorSessionStatus expectedStatus,
            CancellationToken cancellationToken)
        {
            UpdateCalls.Add(new UpdateCall(session, expectedStatus));
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

    private sealed record UpdateCall(
        CollectorSessionAggregate Session,
        CollectorSessionStatus ExpectedStatus);

    private sealed class StubRuntime : ICollectorRuntime
    {
        public void FenceSession(CollectorSessionId sessionId)
        {
        }

        public Task<UnitResult<Error>> StartAsync(
            CollectorRuntimeStartRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UnitResult<Error>> StopAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
