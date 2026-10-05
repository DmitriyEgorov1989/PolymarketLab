using CSharpFunctionalExtensions;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorFailedDatasetFinalization;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.DataCollection.Core.Tests.TestSupport;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Tests.Application.UseCases.CollectorFailedDatasetFinalization;

public sealed class CollectorFailedDatasetFinalizerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-09-01T12:00:00Z");

    [Fact]
    public async Task FinalizeAsync_WhenDeletePolicyInvalidating_ShouldCleanDataset()
    {
        var session = CreateInvalidating();
        var cleanup = new StubCollectorDatasetCleanup();
        var finalizer = new CollectorFailedDatasetFinalizer(
            new StubRepository(session), cleanup, new FixedTimeProvider(Now));

        var result = await finalizer.FinalizeAsync(session, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        cleanup.Calls.Should().Equal(session.Id);
        session.DatasetDisposition.Should().Be(CollectorDatasetDisposition.Deleted);
    }

    [Fact]
    public async Task FinalizeAsync_WhenRetainPolicyInvalidating_ShouldPersistRetainedFailureWithoutCleanup()
    {
        var session = CreateInvalidating(CollectorFailurePolicy.RetainOnFailure);
        var repository = new StubRepository(session);
        var cleanup = new StubCollectorDatasetCleanup();
        var finalizer = new CollectorFailedDatasetFinalizer(
            repository, cleanup, new FixedTimeProvider(Now));

        var result = await finalizer.FinalizeAsync(session, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        session.Status.Should().Be(CollectorSessionStatus.Failed);
        session.DatasetDisposition.Should().Be(CollectorDatasetDisposition.Retained);
        session.RetainUntil.Should().Be(Now.AddHours(1));
        repository.UpdateCalls.Should().Equal(CollectorSessionStatus.Invalidating);
        cleanup.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task FinalizeAsync_WhenRetainCasConflictsWithRetainedWinner_ShouldBeIdempotent()
    {
        var session = CreateInvalidating(CollectorFailurePolicy.RetainOnFailure);
        var winner = CreateInvalidating(CollectorFailurePolicy.RetainOnFailure, session.Id);
        winner.CompleteInvalidation(Now);
        var repository = new StubRepository(session)
        {
            UpdateResult = CollectorSessionUpdateStatus.ConcurrencyConflict,
            ReloadedSession = winner
        };
        var finalizer = new CollectorFailedDatasetFinalizer(
            repository, new StubCollectorDatasetCleanup(), new FixedTimeProvider(Now));

        var result = await finalizer.FinalizeAsync(session, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task FinalizeAsync_WhenRetainCasConflictsWithoutRetainedWinner_ShouldFail()
    {
        var session = CreateInvalidating(CollectorFailurePolicy.RetainOnFailure);
        var repository = new StubRepository(session)
        {
            UpdateResult = CollectorSessionUpdateStatus.ConcurrencyConflict,
            ReloadedSession = CreateInvalidating(
                CollectorFailurePolicy.DeleteOnFailure, session.Id)
        };
        var finalizer = new CollectorFailedDatasetFinalizer(
            repository, new StubCollectorDatasetCleanup(), new FixedTimeProvider(Now));

        var result = await finalizer.FinalizeAsync(session, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.failed_dataset_finalization.session.state_changed");
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 1)]
    public async Task FinalizeAsync_WhenRetained_ShouldCleanOnlyAtOrAfterExpiry(
        int secondsBeforeExpiry,
        int expectedCleanupCalls)
    {
        var session = CreateInvalidating(CollectorFailurePolicy.RetainOnFailure);
        session.CompleteInvalidation(Now);
        var cleanup = new StubCollectorDatasetCleanup();
        var currentTime = session.RetainUntil!.Value.AddSeconds(secondsBeforeExpiry);
        var finalizer = new CollectorFailedDatasetFinalizer(
            new StubRepository(session), cleanup, new FixedTimeProvider(currentTime));

        var result = await finalizer.FinalizeAsync(session, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        cleanup.Calls.Should().HaveCount(expectedCleanupCalls);
    }

    [Fact]
    public async Task FinalizeAsync_WhenCleanupFails_ShouldPreserveError()
    {
        var session = CreateInvalidating();
        var error = new Error("cleanup.failed", "Cleanup failed.", ErrorType.Failure);
        var finalizer = new CollectorFailedDatasetFinalizer(
            new StubRepository(session),
            new StubCollectorDatasetCleanup(error),
            new FixedTimeProvider(Now));

        var result = await finalizer.FinalizeAsync(session, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    private static CollectorSessionAggregate CreateInvalidating(
        CollectorFailurePolicy failurePolicy = CollectorFailurePolicy.DeleteOnFailure,
        CollectorSessionId? sessionId = null)
    {
        var session = CollectorSessionTestFactory.CreateScheduled(
            sessionId: sessionId,
            createdAt: Now.AddMinutes(-2),
            failurePolicy: failurePolicy,
            failureRetentionDuration: failurePolicy == CollectorFailurePolicy.RetainOnFailure
                ? TimeSpan.FromHours(1)
                : null);
        session.BeginInvalidation(
            Now.AddMinutes(-1),
            CollectorStopReason.StartupFailure,
            "collector.failed",
            "Collector failed.");
        return session;
    }

    private sealed class StubRepository(CollectorSessionAggregate session)
        : ICollectorSessionRepository
    {
        public CollectorSessionUpdateStatus UpdateResult { get; init; } =
            CollectorSessionUpdateStatus.Updated;
        public CollectorSessionAggregate? ReloadedSession { get; init; }
        public List<CollectorSessionStatus> UpdateCalls { get; } = [];

        public Task<CollectorSessionAggregate?> GetByIdAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CollectorSessionAggregate?>(ReloadedSession ?? session);

        public Task<Result<CollectorSessionUpdateStatus, Error>> TryUpdateAsync(
            CollectorSessionAggregate updated,
            CollectorSessionStatus expectedStatus,
            CancellationToken cancellationToken)
        {
            UpdateCalls.Add(expectedStatus);
            return Task.FromResult(Result.Success<CollectorSessionUpdateStatus, Error>(UpdateResult));
        }

        public Task<CollectorSessionAggregate?> GetActiveByMarketIdAsync(
            MarketId marketId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectorSessionAggregate?> GetCurrentByMarketIdAsync(
            MarketId marketId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<CollectorSessionAggregate>> GetActiveAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<CollectorSessionInsertStatus, Error>> TryAddAsync(
            CollectorSessionAggregate added, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
