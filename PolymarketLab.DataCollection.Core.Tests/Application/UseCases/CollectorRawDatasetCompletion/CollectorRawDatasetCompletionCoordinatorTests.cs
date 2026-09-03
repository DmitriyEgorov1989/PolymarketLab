using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorRawDatasetCompletion;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionInvalidation;
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

namespace PolymarketLab.DataCollection.Core.Tests.Application.UseCases.CollectorRawDatasetCompletion;

public sealed class CollectorRawDatasetCompletionCoordinatorTests
{
    private static readonly DateTimeOffset CreatedAt =
        DateTimeOffset.Parse("2026-08-27T11:57:00Z");

    [Fact]
    public async Task CompleteAsync_WithConfirmedResolution_ShouldPersistDrainingBeforeStopAndReadAfterCheckpoint()
    {
        var fixture = new Fixture();

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Session.Id,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Calls.Should().Equal(
            "session:draining",
            "runtime:stop",
            "progress:complete",
            "postgres:read",
            "session:awaiting_normalization");
        fixture.Sessions.ExpectedStatuses.Should().Equal(
            CollectorSessionStatus.Running,
            CollectorSessionStatus.Stopping);
        fixture.Session.Status.Should().Be(CollectorSessionStatus.Stopping);
        fixture.Session.Phase.Should().Be(CollectorSessionPhase.AwaitingNormalization);
        fixture.Session.AwaitingNormalizationAt.Should().Be(CreatedAt.AddMinutes(20));
        fixture.Invalidation.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1250, 1250, 1250, 1250, true)]
    [InlineData(0, 0, 0, 0, false)]
    [InlineData(1250, 1249, 1249, 1249, false)]
    [InlineData(1250, 1250, 1249, 1249, false)]
    [InlineData(1250, 1250, 1250, 1249, false)]
    public async Task CompleteAsync_WithExactEqualityMatrix_ShouldAcceptOnlyEqualCounters(
        long received,
        long enqueued,
        long persisted,
        long rawCount,
        bool expectedSuccess)
    {
        var fixture = new Fixture(new CollectorSessionProgress(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            2,
            received,
            enqueued,
            persisted,
            rawCount,
            null,
            0));

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Session.Id,
            CancellationToken.None);

        if (expectedSuccess)
        {
            result.IsSuccess.Should().BeTrue();
            fixture.Session.Phase.Should().Be(CollectorSessionPhase.AwaitingNormalization);
            fixture.Invalidation.Calls.Should().BeEmpty();
            return;
        }

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.raw_completion.accounting_mismatch");
        fixture.Calls.Should().NotContain("session:awaiting_normalization");
        fixture.Session.Status.Should().Be(CollectorSessionStatus.Invalidating);
        fixture.Session.Phase.Should().Be(CollectorSessionPhase.Cleaning);
        fixture.Invalidation.Calls.Should().ContainSingle(call =>
            call.Reason == CollectorStopReason.PersistenceFailure
            && call.Failure.Code == "collector.raw_completion.accounting_mismatch"
            && call.Failure.Message.Contains("received=")
            && call.Failure.Message.Contains("raw="));
    }

    [Fact]
    public async Task CompleteAsync_WhenRuntimeStopFails_ShouldInvalidateWithoutDrainOrRead()
    {
        var fixture = new Fixture();
        fixture.Runtime.StopResult = UnitResult.Failure(new Error(
            "collector.runtime.stop.timeout",
            "The collector runtime stop timed out.",
            ErrorType.Failure));

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Session.Id,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.runtime.stop.timeout");
        fixture.Calls.Should().Equal(
            "session:draining",
            "runtime:stop",
            "invalidation",
            "runtime:stop");
        fixture.ProgressCompletion.CallCount.Should().Be(0);
        fixture.Progress.ReadCount.Should().Be(0);
        fixture.Invalidation.Calls.Should().ContainSingle(call =>
            call.Reason == CollectorStopReason.PersistenceFailure
            && call.Failure.Code == "collector.runtime.stop.timeout");
    }

    [Fact]
    public async Task CompleteAsync_WhenProgressCompletionFails_ShouldInvalidateWithoutPostgreSqlRead()
    {
        var fixture = new Fixture();
        fixture.ProgressCompletion.Result = UnitResult.Failure(new Error(
            "collector.progress.persistence_timeout",
            "Collector session progress did not persist within the timeout.",
            ErrorType.Failure));

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Session.Id,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.progress.persistence_timeout");
        fixture.Calls.Should().Equal(
            "session:draining",
            "runtime:stop",
            "progress:complete",
            "invalidation",
            "runtime:stop");
        fixture.Progress.ReadCount.Should().Be(0);
        fixture.Invalidation.Calls.Should().ContainSingle(call =>
            call.Reason == CollectorStopReason.PersistenceFailure
            && call.Failure.Code == "collector.progress.persistence_timeout");
    }

    [Fact]
    public async Task CompleteAsync_WhenProgressReadFails_ShouldDurablyInvalidate()
    {
        var fixture = new Fixture();
        fixture.Progress.Exception = new InvalidOperationException(
            "PostgreSQL progress read failed.");

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Session.Id,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.raw_completion.progress_read_failed");
        fixture.Calls.Should().Equal(
            "session:draining",
            "runtime:stop",
            "progress:complete",
            "postgres:read",
            "invalidation",
            "runtime:stop");
        fixture.Invalidation.Calls.Should().ContainSingle(call =>
            call.Reason == CollectorStopReason.PersistenceFailure
            && call.Failure.Code == "collector.raw_completion.progress_read_failed"
            && !call.Failure.Message.Contains("PostgreSQL progress read failed."));
    }

    [Fact]
    public async Task CompleteAsync_WhenSessionNotFound_ShouldReturnNotFoundAfterInvalidation()
    {
        var fixture = new Fixture();
        fixture.Sessions.OnGetById = () => null;
        fixture.Invalidation.FixedResult =
            Result.Success<CollectorSessionAggregate?, Error>(null);

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Session.Id,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.raw_completion.session_not_found");
        fixture.Invalidation.Calls.Should().ContainSingle();
        fixture.Runtime.StopCallCount.Should().Be(0);
        fixture.ProgressCompletion.CallCount.Should().Be(0);
        fixture.Progress.ReadCount.Should().Be(0);
    }

    [Fact]
    public async Task CompleteAsync_WhenResolutionNotConfirmed_ShouldInvalidateWithoutDrain()
    {
        var session = CollectorSessionTestFactory.CreateRunning(createdAt: CreatedAt);
        session.MarkCollectingWindow();
        session.MarkAwaitingResolution();
        var fixture = new Fixture(session: session);

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Session.Id,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.raw_completion.resolution_not_confirmed");
        fixture.Calls.Should().Equal("invalidation", "runtime:stop");
        fixture.Sessions.TryUpdateCount.Should().Be(0);
        fixture.ProgressCompletion.CallCount.Should().Be(0);
        fixture.Progress.ReadCount.Should().Be(0);
        fixture.Invalidation.Calls.Should().ContainSingle(call =>
            call.Reason == CollectorStopReason.PersistenceFailure
            && call.Failure.Code == "collector.raw_completion.resolution_not_confirmed");
    }

    [Fact]
    public async Task CompleteAsync_WhenSessionAlreadyAwaitingNormalization_ShouldSucceedIdempotently()
    {
        var session = CreateConfirmedSession();
        session.MarkStopping().IsSuccess.Should().BeTrue();
        session.MarkAwaitingNormalization(session.EventEndsAt!.Value.AddSeconds(1))
            .IsSuccess.Should().BeTrue();
        var fixture = new Fixture(session: session);

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Session.Id,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Calls.Should().Equal(
            "runtime:stop",
            "progress:complete",
            "postgres:read");
        fixture.Sessions.TryUpdateCount.Should().Be(0);
        fixture.Session.Phase.Should().Be(CollectorSessionPhase.AwaitingNormalization);
        fixture.Invalidation.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task CompleteAsync_WhenSessionAlreadyDrainingRaw_ShouldContinueWithoutRepeatedTransition()
    {
        var session = CreateConfirmedSession();
        session.MarkStopping().IsSuccess.Should().BeTrue();
        var fixture = new Fixture(session: session);

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Session.Id,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Calls.Should().Equal(
            "runtime:stop",
            "progress:complete",
            "postgres:read",
            "session:awaiting_normalization");
        fixture.Sessions.ExpectedStatuses.Should().Equal(CollectorSessionStatus.Stopping);
        fixture.Session.Status.Should().Be(CollectorSessionStatus.Stopping);
        fixture.Session.Phase.Should().Be(CollectorSessionPhase.AwaitingNormalization);
    }

    [Fact]
    public async Task CompleteAsync_WhenDrainingCasAlwaysConflicts_ShouldRetryThreeTimesAndInvalidate()
    {
        var fixture = new Fixture();
        fixture.Sessions.UpdateStatus = CollectorSessionUpdateStatus.ConcurrencyConflict;
        fixture.Sessions.OnGetById = CreateConfirmedSession;

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Session.Id,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.raw_completion.state_transition_conflict");
        fixture.Sessions.TryUpdateCount.Should().Be(3);
        fixture.Sessions.ReadCount.Should().Be(4);
        fixture.ProgressCompletion.CallCount.Should().Be(0);
        fixture.Progress.ReadCount.Should().Be(0);
        fixture.Calls.Should().Equal(
            "session:draining",
            "session:draining",
            "session:draining",
            "invalidation",
            "runtime:stop");
        fixture.Invalidation.Calls.Should().ContainSingle(call =>
            call.Reason == CollectorStopReason.PersistenceFailure
            && call.Failure.Code == "collector.raw_completion.state_transition_conflict");
    }

    private static CollectorSessionAggregate CreateConfirmedSession()
    {
        var session = CollectorSessionTestFactory.CreateRunning(createdAt: CreatedAt);
        session.MarkCollectingWindow();
        session.MarkAwaitingResolution();
        var confirmation = session.ConfirmResolution(
            session.EventEndsAt!.Value,
            session.EventEndsAt.Value,
            new ResolutionWinner("1001", "Yes"),
            2);
        confirmation.IsSuccess.Should().BeTrue();
        return session;
    }

    private sealed class Fixture
    {
        public Fixture(
            CollectorSessionProgress? progress = null,
            CollectorSessionAggregate? session = null)
        {
            Session = session ?? CreateConfirmedSession();
            Calls = [];
            Sessions = new SessionRepository(Session, Calls);
            Runtime = new StubRuntime(Calls);
            ProgressCompletion = new StubProgressCompletion(Calls);
            Progress = new ProgressRepository(
                progress ?? new CollectorSessionProgress(
                    Session.Id,
                    2,
                    1250,
                    1250,
                    1250,
                    1250,
                    null,
                    0),
                Calls);
            Invalidation = new InvalidationCoordinator(Session, Calls);
            Coordinator = new CollectorRawDatasetCompletionCoordinator(
                Sessions,
                Runtime,
                ProgressCompletion,
                Progress,
                Invalidation,
                new FixedTimeProvider(CreatedAt.AddMinutes(20)),
                NullLogger<CollectorRawDatasetCompletionCoordinator>.Instance);
        }

        public CollectorSessionAggregate Session { get; }
        public List<string> Calls { get; }
        public SessionRepository Sessions { get; }
        public StubRuntime Runtime { get; }
        public StubProgressCompletion ProgressCompletion { get; }
        public ProgressRepository Progress { get; }
        public InvalidationCoordinator Invalidation { get; }
        public ICollectorRawDatasetCompletionCoordinator Coordinator { get; }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class SessionRepository(
        CollectorSessionAggregate session,
        List<string> calls) : ICollectorSessionRepository
    {
        public List<CollectorSessionStatus> ExpectedStatuses { get; } = [];
        public int TryUpdateCount { get; private set; }
        public int ReadCount { get; private set; }
        public CollectorSessionUpdateStatus UpdateStatus { get; set; } =
            CollectorSessionUpdateStatus.Updated;
        public Func<CollectorSessionAggregate?>? OnGetById { get; set; }

        public Task<CollectorSessionAggregate?> GetByIdAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult<CollectorSessionAggregate?>(
                OnGetById is not null ? OnGetById() : session);
        }

        public Task<Result<CollectorSessionUpdateStatus, Error>> TryUpdateAsync(
            CollectorSessionAggregate current,
            CollectorSessionStatus expectedStatus,
            CancellationToken cancellationToken)
        {
            TryUpdateCount++;
            ExpectedStatuses.Add(expectedStatus);
            if (expectedStatus == CollectorSessionStatus.Running
                && current.Status == CollectorSessionStatus.Stopping
                && current.Phase == CollectorSessionPhase.DrainingRaw)
            {
                calls.Add("session:draining");
            }
            else if (expectedStatus == CollectorSessionStatus.Stopping
                     && current.Status == CollectorSessionStatus.Stopping
                     && current.Phase == CollectorSessionPhase.AwaitingNormalization)
            {
                calls.Add("session:awaiting_normalization");
            }

            return Task.FromResult(Result.Success<CollectorSessionUpdateStatus, Error>(
                UpdateStatus));
        }

        public Task<CollectorSessionAggregate?> GetExclusiveAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CollectorSessionAggregate?> GetActiveByMarketIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CollectorSessionAggregate?> GetCurrentByMarketIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyCollection<CollectorSessionAggregate>> GetActiveAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<CollectorSessionInsertStatus, Error>> TryAddAsync(
            CollectorSessionAggregate current,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubRuntime(List<string> calls) : ICollectorRuntime
    {
        public UnitResult<Error> StopResult { get; set; } = UnitResult.Success<Error>();
        public int StopCallCount { get; private set; }

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
            StopCallCount++;
            calls.Add("runtime:stop");
            return Task.FromResult(StopResult);
        }
    }

    private sealed class StubProgressCompletion(List<string> calls)
        : ICollectorSessionProgressCompletion
    {
        public UnitResult<Error> Result { get; set; } = UnitResult.Success<Error>();
        public int CallCount { get; private set; }

        public Task<UnitResult<Error>> CompleteAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            calls.Add("progress:complete");
            return Task.FromResult(Result);
        }
    }

    private sealed class ProgressRepository(
        CollectorSessionProgress progress,
        List<string> calls) : ICollectorSessionProgressRepository
    {
        public int ReadCount { get; private set; }
        public Exception? Exception { get; set; }

        public Task<CollectorSessionProgress> GetAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken)
        {
            ReadCount++;
            calls.Add("postgres:read");
            if (Exception is not null)
                throw Exception;
            return Task.FromResult(progress);
        }

        public Task CheckpointAsync(
            CollectorSessionProgressCheckpoint checkpoint,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class InvalidationCoordinator(
        CollectorSessionAggregate session,
        List<string> calls) : ICollectorSessionInvalidationCoordinator
    {
        public List<InvalidationCall> Calls { get; } = [];
        public Result<CollectorSessionAggregate?, Error>? FixedResult { get; set; }

        public Task<Result<CollectorSessionAggregate?, Error>> InvalidateAsync(
            CollectorSessionId sessionId,
            DateTimeOffset occurredAt,
            CollectorStopReason reason,
            Error failure,
            CancellationToken cancellationToken)
        {
            Calls.Add(new InvalidationCall(occurredAt, reason, failure));
            calls.Add("invalidation");
            if (FixedResult is not null)
                return Task.FromResult(FixedResult.Value);

            var transition = session.BeginInvalidation(
                occurredAt,
                reason,
                failure.Code,
                failure.Message);
            return Task.FromResult(transition.IsFailure
                ? Result.Failure<CollectorSessionAggregate?, Error>(transition.Error)
                : Result.Success<CollectorSessionAggregate?, Error>(session));
        }
    }

    private sealed record InvalidationCall(
        DateTimeOffset OccurredAt,
        CollectorStopReason Reason,
        Error Failure);
}
