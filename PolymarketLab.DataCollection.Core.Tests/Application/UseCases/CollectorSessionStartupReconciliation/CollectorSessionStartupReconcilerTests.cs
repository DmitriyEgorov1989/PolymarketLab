using CSharpFunctionalExtensions;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionStartupReconciliation;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Tests.Application.UseCases.CollectorSessionStartupReconciliation;

public sealed class CollectorSessionStartupReconcilerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReconcileAsync_WithActiveSessions_ShouldInterruptAll()
    {
        var sessions = new[]
        {
            CreateSession(CollectorSessionStatus.Starting),
            CreateSession(CollectorSessionStatus.Running),
            CreateSession(CollectorSessionStatus.Stopping)
        };
        var repository = new StubRepository(sessions);
        var reconciler = new CollectorSessionStartupReconciler(
            repository,
            new FixedTimeProvider(Now));

        var result = await reconciler.ReconcileAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repository.UpdateCalls.Should().HaveCount(3);
        repository.UpdateCalls.Select(call => call.ExpectedStatus).Should().Equal(
            CollectorSessionStatus.Starting,
            CollectorSessionStatus.Running,
            CollectorSessionStatus.Stopping);
        repository.UpdateCalls.Should().OnlyContain(call =>
            call.Status == CollectorSessionStatus.Interrupted
            && call.Reason == CollectorStopReason.ProcessTerminated);
    }

    [Fact]
    public async Task ReconcileAsync_WhenStatusChanges_ShouldReloadAndRetry()
    {
        var sessionId = CollectorSessionId.Create(Guid.NewGuid()).Value;
        var marketId = MarketId.Create(Guid.NewGuid()).Value;
        var starting = CreateSession(
            sessionId,
            marketId,
            CollectorSessionStatus.Starting);
        var running = CreateSession(
            sessionId,
            marketId,
            CollectorSessionStatus.Running);
        var repository = new StubRepository([starting], running);
        repository.UpdateResults.Enqueue(
            CollectorSessionUpdateStatus.ConcurrencyConflict);
        repository.UpdateResults.Enqueue(CollectorSessionUpdateStatus.Updated);
        var reconciler = new CollectorSessionStartupReconciler(
            repository,
            new FixedTimeProvider(Now));

        var result = await reconciler.ReconcileAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repository.UpdateCalls.Should().HaveCount(2);
        repository.UpdateCalls[0].ExpectedStatus.Should()
            .Be(CollectorSessionStatus.Starting);
        repository.UpdateCalls[1].ExpectedStatus.Should()
            .Be(CollectorSessionStatus.Running);
    }

    [Fact]
    public async Task ReconcileAsync_WhenConflictsRemainActive_ShouldReturnError()
    {
        var sessionId = CollectorSessionId.Create(Guid.NewGuid()).Value;
        var marketId = MarketId.Create(Guid.NewGuid()).Value;
        var sessions = Enumerable.Range(0, 4)
            .Select(_ => CreateSession(
                sessionId,
                marketId,
                CollectorSessionStatus.Running))
            .ToArray();
        var repository = new StubRepository([sessions[0]], sessions[1..]);
        repository.UpdateResults.Enqueue(
            CollectorSessionUpdateStatus.ConcurrencyConflict);
        repository.UpdateResults.Enqueue(
            CollectorSessionUpdateStatus.ConcurrencyConflict);
        repository.UpdateResults.Enqueue(
            CollectorSessionUpdateStatus.ConcurrencyConflict);
        var reconciler = new CollectorSessionStartupReconciler(
            repository,
            new FixedTimeProvider(Now));

        var result = await reconciler.ReconcileAsync(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(
            "collector.session.reconciliation.state_changed");
        repository.UpdateCalls.Should().HaveCount(3);
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
        var session = CollectorSessionAggregate.Create(
            sessionId,
            marketId,
            Now.AddMinutes(-1)).Value;
        if (status is CollectorSessionStatus.Running or CollectorSessionStatus.Stopping)
            session.MarkRunning(Now.AddSeconds(-30));
        if (status == CollectorSessionStatus.Stopping)
            session.MarkStopping();
        return session;
    }

    private sealed class StubRepository(
        IReadOnlyCollection<CollectorSessionAggregate> activeSessions,
        params CollectorSessionAggregate[] reloadSessions)
        : ICollectorSessionRepository
    {
        private readonly Queue<CollectorSessionAggregate> _reloadSessions =
            new(reloadSessions);

        public Queue<Result<CollectorSessionUpdateStatus, Error>> UpdateResults { get; } = [];
        public List<UpdateCall> UpdateCalls { get; } = [];

        public Task<IReadOnlyCollection<CollectorSessionAggregate>> GetActiveAsync(
            CancellationToken cancellationToken) => Task.FromResult(activeSessions);

        public Task<CollectorSessionAggregate?> GetByIdAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<CollectorSessionAggregate?>(
                _reloadSessions.TryDequeue(out var session) ? session : null);
        }

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

        public Task<Result<CollectorSessionInsertStatus, Error>> TryAddAsync(
            CollectorSessionAggregate session,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed record UpdateCall(
        CollectorSessionStatus Status,
        CollectorSessionStatus ExpectedStatus,
        CollectorStopReason? Reason);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
