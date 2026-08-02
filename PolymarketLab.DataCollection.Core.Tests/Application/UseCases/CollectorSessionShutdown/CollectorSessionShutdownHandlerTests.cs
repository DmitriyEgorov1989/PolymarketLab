using CSharpFunctionalExtensions;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionShutdown;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Tests.Application.UseCases.CollectorSessionShutdown;

public sealed class CollectorSessionShutdownHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MarkStoppingAsync_WithRunningSession_ShouldPersistStopping()
    {
        var session = CreateRunningSession();
        var repository = new StubRepository(session);
        var handler = CreateHandler(repository);

        var result = await handler.MarkStoppingAsync(
            session.Id,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repository.UpdateCalls.Should().ContainSingle();
        repository.UpdateCalls[0].Status.Should()
            .Be(CollectorSessionStatus.Stopping);
        repository.UpdateCalls[0].ExpectedStatus.Should()
            .Be(CollectorSessionStatus.Running);
    }

    [Fact]
    public async Task MarkStoppedAsync_WithStoppingSession_ShouldPersistStopped()
    {
        var session = CreateRunningSession();
        session.MarkStopping();
        var repository = new StubRepository(session);
        var handler = CreateHandler(repository);

        var result = await handler.MarkStoppedAsync(
            session.Id,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repository.UpdateCalls.Should().ContainSingle();
        repository.UpdateCalls[0].Status.Should()
            .Be(CollectorSessionStatus.Stopped);
        repository.UpdateCalls[0].Reason.Should()
            .Be(CollectorStopReason.ApplicationShutdown);
        repository.UpdateCalls[0].ExpectedStatus.Should()
            .Be(CollectorSessionStatus.Stopping);
    }

    [Fact]
    public async Task MarkStoppedAsync_WhenUpdateConflicts_ShouldReloadAndRetry()
    {
        var first = CreateRunningSession();
        var replacement = CollectorSessionAggregate.Create(
            first.Id,
            first.MarketId,
            Now.AddMinutes(-1)).Value;
        replacement.MarkRunning(Now.AddSeconds(-30));
        replacement.MarkStopping();
        var repository = new StubRepository(first, replacement);
        repository.UpdateResults.Enqueue(
            CollectorSessionUpdateStatus.ConcurrencyConflict);
        repository.UpdateResults.Enqueue(CollectorSessionUpdateStatus.Updated);
        var handler = CreateHandler(repository);

        var result = await handler.MarkStoppedAsync(
            first.Id,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repository.UpdateCalls.Should().HaveCount(2);
        repository.UpdateCalls[0].ExpectedStatus.Should()
            .Be(CollectorSessionStatus.Running);
        repository.UpdateCalls[1].ExpectedStatus.Should()
            .Be(CollectorSessionStatus.Stopping);
    }

    [Fact]
    public async Task MarkFailedAsync_WithStoppingSession_ShouldPersistPersistenceFailure()
    {
        var session = CreateRunningSession();
        session.MarkStopping();
        var repository = new StubRepository(session);
        var handler = CreateHandler(repository);
        var error = new Error(
            "raw_messages.persistence.failed",
            "Raw persistence failed.",
            ErrorType.Failure);

        var result = await handler.MarkFailedAsync(
            session.Id,
            error,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repository.UpdateCalls.Should().ContainSingle();
        repository.UpdateCalls[0].Status.Should()
            .Be(CollectorSessionStatus.Failed);
        repository.UpdateCalls[0].Reason.Should()
            .Be(CollectorStopReason.PersistenceFailure);
        repository.UpdateCalls[0].FailureCode.Should()
            .Be("raw_messages.persistence.failed");
    }

    private static CollectorSessionShutdownHandler CreateHandler(
        StubRepository repository)
    {
        return new CollectorSessionShutdownHandler(
            repository,
            new FixedTimeProvider(Now));
    }

    private static CollectorSessionAggregate CreateRunningSession()
    {
        var session = CollectorSessionAggregate.Create(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            MarketId.Create(Guid.NewGuid()).Value,
            Now.AddMinutes(-1)).Value;
        session.MarkRunning(Now.AddSeconds(-30));
        return session;
    }

    private sealed class StubRepository(
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

        public Task<Result<CollectorSessionUpdateStatus, Error>> TryUpdateAsync(
            CollectorSessionAggregate session,
            CollectorSessionStatus expectedStatus,
            CancellationToken cancellationToken)
        {
            UpdateCalls.Add(new UpdateCall(
                session.Status,
                expectedStatus,
                session.StopReason,
                session.FailureCode));
            return Task.FromResult(
                UpdateResults.TryDequeue(out var result)
                    ? result
                    : Result.Success<CollectorSessionUpdateStatus, Error>(
                        CollectorSessionUpdateStatus.Updated));
        }

        public Task<CollectorSessionAggregate?> GetActiveByMarketIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyCollection<CollectorSessionAggregate>> GetActiveAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<CollectorSessionInsertStatus, Error>> TryAddAsync(
            CollectorSessionAggregate session,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed record UpdateCall(
        CollectorSessionStatus Status,
        CollectorSessionStatus ExpectedStatus,
        CollectorStopReason? Reason,
        string? FailureCode);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
