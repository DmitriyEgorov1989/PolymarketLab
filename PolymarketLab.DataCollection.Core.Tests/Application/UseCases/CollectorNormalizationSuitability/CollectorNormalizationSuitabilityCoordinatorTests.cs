using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorNormalizationSuitability;
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

namespace PolymarketLab.DataCollection.Core.Tests.Application.UseCases.CollectorNormalizationSuitability;

public sealed class CollectorNormalizationSuitabilityCoordinatorTests
{
    private static readonly DateTimeOffset CreatedAt =
        DateTimeOffset.Parse("2026-09-03T11:57:00Z");

    [Fact]
    public void Suitability_WithMissingSnapshotRows_ShouldExposeMissingCount()
    {
        var suitability = new NormalizationSuitability(
            RawCount: 1250,
            LedgerCount: 1240,
            ProcessedCount: 1230,
            PendingCount: 7,
            ProcessingCount: 3,
            UnsupportedCount: 0,
            InvalidCount: 0,
            FailedCount: 0,
            ResolutionRawItemProcessed: false);

        suitability.MissingCount.Should().Be(10);
    }

    [Fact]
    public async Task EvaluateAsync_WithAllProcessedAndResolutionProvenance_ShouldStopAsMarketClosed()
    {
        var fixture = new Fixture();

        var result = await fixture.Coordinator.EvaluateAsync(
            fixture.Session.Id,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Session.Status.Should().Be(CollectorSessionStatus.Stopped);
        fixture.Session.Phase.Should().BeNull();
        fixture.Session.StopReason.Should().Be(CollectorStopReason.MarketClosed);
        fixture.Suitability.Calls.Should().ContainSingle(call =>
            call.SessionId == fixture.Session.Id && call.ProjectionVersion == 3);
        fixture.Sessions.ExpectedStatuses.Should().Equal(
            CollectorSessionStatus.Stopping);
        fixture.Invalidation.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(7, 3, 0)]
    [InlineData(0, 0, 10)]
    public async Task EvaluateAsync_BeforeDeadlineWithIncompleteLedger_ShouldWait(
        long pending,
        long processing,
        long missing)
    {
        var suitability = new NormalizationSuitability(
            RawCount: 1250,
            LedgerCount: 1250 - missing,
            ProcessedCount: 1250 - pending - processing - missing,
            PendingCount: pending,
            ProcessingCount: processing,
            UnsupportedCount: 0,
            InvalidCount: 0,
            FailedCount: 0,
            ResolutionRawItemProcessed: true);
        var fixture = new Fixture(suitability: suitability);

        var result = await fixture.Coordinator.EvaluateAsync(
            fixture.Session.Id,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Session.Status.Should().Be(CollectorSessionStatus.Stopping);
        fixture.Session.Phase.Should().Be(CollectorSessionPhase.AwaitingNormalization);
        fixture.Sessions.TryUpdateCount.Should().Be(0);
        fixture.Invalidation.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, 0, 0, "collector.normalization_suitability.unsupported")]
    [InlineData(0, 1, 0, "collector.normalization_suitability.invalid")]
    [InlineData(0, 0, 1, "collector.normalization_suitability.failed")]
    public async Task EvaluateAsync_WithTerminalLedgerStatus_ShouldInvalidateImmediately(
        long unsupported,
        long invalid,
        long failed,
        string expectedCode)
    {
        var suitability = new NormalizationSuitability(
            RawCount: 1250,
            LedgerCount: 1250,
            ProcessedCount: 1250 - unsupported - invalid - failed,
            PendingCount: 0,
            ProcessingCount: 0,
            UnsupportedCount: unsupported,
            InvalidCount: invalid,
            FailedCount: failed,
            ResolutionRawItemProcessed: true);
        var fixture = new Fixture(suitability: suitability);

        var result = await fixture.Coordinator.EvaluateAsync(
            fixture.Session.Id,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(expectedCode);
        fixture.Session.Status.Should().Be(CollectorSessionStatus.Invalidating);
        fixture.Sessions.TryUpdateCount.Should().Be(0);
        fixture.Invalidation.Calls.Should().ContainSingle(call =>
            call.Reason == CollectorStopReason.PersistenceFailure
            && call.Failure.Code == expectedCode);
    }

    [Fact]
    public async Task EvaluateAsync_AtExactDeadlineWithIncompleteLedger_ShouldInvalidateAsTimeout()
    {
        var fixture = new Fixture(suitability: Fixture.Incomplete);
        fixture.Time.SetUtcNow(fixture.AwaitingNormalizationAt.AddMinutes(5));

        var result = await fixture.Coordinator.EvaluateAsync(
            fixture.Session.Id,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.normalization_suitability.timeout");
        fixture.Sessions.TryUpdateCount.Should().Be(0);
        fixture.Invalidation.Calls.Should().ContainSingle(call =>
            call.Reason == CollectorStopReason.PersistenceFailure
             && call.Failure.Code == "collector.normalization_suitability.timeout");
    }

    [Fact]
    public async Task EvaluateAsync_AtExactDeadlineWithFullyProcessedLedger_ShouldInvalidateAsTimeout()
    {
        var fixture = new Fixture();
        fixture.Time.SetUtcNow(fixture.AwaitingNormalizationAt.AddMinutes(5));

        var result = await fixture.Coordinator.EvaluateAsync(
            fixture.Session.Id,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.normalization_suitability.timeout");
        fixture.Sessions.TryUpdateCount.Should().Be(0);
        fixture.Invalidation.Calls.Should().ContainSingle(call =>
            call.Reason == CollectorStopReason.PersistenceFailure
            && call.Failure.Code == "collector.normalization_suitability.timeout");
    }

    [Fact]
    public async Task EvaluateAsync_OneTickBeforeDeadlineWithIncompleteLedger_ShouldWait()
    {
        var fixture = new Fixture(suitability: Fixture.Incomplete);
        fixture.Time.SetUtcNow(
            fixture.AwaitingNormalizationAt.AddMinutes(5).AddTicks(-1));

        var result = await fixture.Coordinator.EvaluateAsync(
            fixture.Session.Id,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Session.Status.Should().Be(CollectorSessionStatus.Stopping);
        fixture.Session.Phase.Should().Be(CollectorSessionPhase.AwaitingNormalization);
        fixture.Sessions.TryUpdateCount.Should().Be(0);
        fixture.Invalidation.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task EvaluateAsync_AtOldEventDeadlineAfterLaterRawDrain_ShouldStillWait()
    {
        var fixture = new Fixture(suitability: Fixture.Incomplete);
        fixture.Time.SetUtcNow(fixture.EventEndsAt.AddMinutes(5));

        var result = await fixture.Coordinator.EvaluateAsync(
            fixture.Session.Id,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Session.Status.Should().Be(CollectorSessionStatus.Stopping);
        fixture.Session.Phase.Should().Be(CollectorSessionPhase.AwaitingNormalization);
        fixture.Invalidation.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task EvaluateAsync_WithProcessedCardinalityAndMissingResolutionProvenance_ShouldInvalidate()
    {
        var fixture = new Fixture(suitability: Fixture.FullyProcessed with
        {
            ResolutionRawItemProcessed = false
        });

        var result = await fixture.Coordinator.EvaluateAsync(
            fixture.Session.Id,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should()
            .Be("collector.normalization_suitability.resolution_provenance_invalid");
        fixture.Sessions.TryUpdateCount.Should().Be(0);
        fixture.Invalidation.Calls.Should().ContainSingle(call =>
            call.Reason == CollectorStopReason.PersistenceFailure
            && call.Failure.Code ==
            "collector.normalization_suitability.resolution_provenance_invalid");
    }

    [Fact]
    public async Task EvaluateAsync_WithRuntimeVersionMismatch_ShouldInvalidateWithoutReadingLedger()
    {
        var fixture = new Fixture(projectionVersion: 4);

        var result = await fixture.Coordinator.EvaluateAsync(
            fixture.Session.Id,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should()
            .Be("collector.normalization_suitability.projection_version_mismatch");
        fixture.Suitability.CallCount.Should().Be(0);
        fixture.Sessions.TryUpdateCount.Should().Be(0);
        fixture.Invalidation.Calls.Should().ContainSingle(call =>
            call.Reason == CollectorStopReason.PersistenceFailure
            && call.Failure.Code ==
            "collector.normalization_suitability.projection_version_mismatch");
    }

    [Fact]
    public async Task EvaluateAsync_WithLegacyNullProjectionVersion_ShouldInvalidateWithoutReadingLedger()
    {
        var fixture = new Fixture(session: CreateLegacyAwaitingNormalizationSession());

        var result = await fixture.Coordinator.EvaluateAsync(
            fixture.Session.Id,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should()
            .Be("collector.normalization_suitability.projection_version_missing");
        fixture.Suitability.CallCount.Should().Be(0);
        fixture.Sessions.TryUpdateCount.Should().Be(0);
        fixture.Invalidation.Calls.Should().ContainSingle(call =>
            call.Reason == CollectorStopReason.PersistenceFailure
            && call.Failure.Code ==
             "collector.normalization_suitability.projection_version_missing");
    }

    [Fact]
    public async Task EvaluateAsync_WithMissingAwaitingNormalizationAt_ShouldInvalidateWithoutReadingLedger()
    {
        var session = CreateLegacyAwaitingNormalizationSession();
        SetValue(session, nameof(CollectorSessionAggregate.ProjectionVersion), 3);
        var fixture = new Fixture(session: session);

        var result = await fixture.Coordinator.EvaluateAsync(
            fixture.Session.Id,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should()
            .Be("collector.normalization_suitability.awaiting_normalization_at_missing");
        fixture.Suitability.CallCount.Should().Be(0);
        fixture.Invalidation.Calls.Should().ContainSingle(call =>
            call.Reason == CollectorStopReason.PersistenceFailure
            && call.Failure.Code ==
            "collector.normalization_suitability.awaiting_normalization_at_missing");
    }

    [Fact]
    public async Task EvaluateAsync_WhenReaderThrows_ShouldDurablyInvalidateAndReturnSafeFailure()
    {
        var fixture = new Fixture();
        fixture.Suitability.Exception = new InvalidOperationException(
            "database read failed");

        var result = await fixture.Coordinator.EvaluateAsync(
            fixture.Session.Id,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.normalization_suitability.read_failed");
        result.Error.Message.Should().NotContain("database read failed");
        fixture.Logger.Errors.Should().ContainSingle(error =>
            error.Exception != null
            && error.Exception.GetType() == typeof(InvalidOperationException)
            && error.Exception.Message.Contains("database read failed"));
        fixture.Invalidation.Calls.Should().ContainSingle(call =>
            call.Reason == CollectorStopReason.PersistenceFailure
            && call.Failure.Code == "collector.normalization_suitability.read_failed"
            && !call.Failure.Message.Contains("database read failed"));
    }

    [Fact]
    public async Task EvaluateAsync_WhenCompletionCasConflicts_ShouldReloadAndRetryThreeTimes()
    {
        var fixture = new Fixture();
        fixture.Sessions.UpdateStatus = CollectorSessionUpdateStatus.ConcurrencyConflict;
        fixture.Sessions.OnGetById = CreateAwaitingNormalizationSession;

        var result = await fixture.Coordinator.EvaluateAsync(
            fixture.Session.Id,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should()
            .Be("collector.normalization_suitability.state_transition_conflict");
        fixture.Sessions.TryUpdateCount.Should().Be(3);
        fixture.Sessions.ReadCount.Should().Be(4);
        fixture.Invalidation.Calls.Should().ContainSingle(call =>
            call.Reason == CollectorStopReason.PersistenceFailure
            && call.Failure.Code ==
            "collector.normalization_suitability.state_transition_conflict");
    }

    [Fact]
    public async Task EvaluateAsync_WhenAnotherTickAlreadyStoppedSession_ShouldSucceedIdempotently()
    {
        var fixture = new Fixture();
        fixture.Sessions.UpdateStatus = CollectorSessionUpdateStatus.ConcurrencyConflict;
        var stopped = CreateStoppedAsMarketClosed();
        var reads = 0;
        fixture.Sessions.OnGetById = () =>
        {
            reads++;
            return reads == 1 ? fixture.Session : stopped;
        };

        var result = await fixture.Coordinator.EvaluateAsync(
            fixture.Session.Id,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Sessions.TryUpdateCount.Should().Be(1);
        fixture.Invalidation.Calls.Should().BeEmpty();
    }

    private static CollectorSessionAggregate CreateAwaitingNormalizationSession()
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
        session.MarkStopping().IsSuccess.Should().BeTrue();
        session.MarkAwaitingNormalization(session.EventEndsAt.Value.AddSeconds(1))
            .IsSuccess.Should().BeTrue();
        return session;
    }

    private static CollectorSessionAggregate CreateStoppedAsMarketClosed()
    {
        var session = CreateAwaitingNormalizationSession();
        var stop = session.Stop(
            session.EventEndsAt!.Value.AddMinutes(1),
            CollectorStopReason.MarketClosed);
        stop.IsSuccess.Should().BeTrue();
        return session;
    }

    private static CollectorSessionAggregate CreateLegacyAwaitingNormalizationSession()
    {
        var session = (CollectorSessionAggregate)Activator.CreateInstance(
            typeof(CollectorSessionAggregate),
            nonPublic: true)!;
        SetValue(session, nameof(CollectorSessionAggregate.Id),
            CollectorSessionId.Create(Guid.NewGuid()).Value);
        SetValue(session, nameof(CollectorSessionAggregate.CreatedAt), CreatedAt);
        SetValue(session, nameof(CollectorSessionAggregate.StartedAt),
            CreatedAt.AddSeconds(1));
        SetValue(session, nameof(CollectorSessionAggregate.EventEndsAt),
            CreatedAt.AddMinutes(8));
        SetValue(session, nameof(CollectorSessionAggregate.Status),
            CollectorSessionStatus.Stopping);
        SetValue(session, nameof(CollectorSessionAggregate.Phase),
            CollectorSessionPhase.AwaitingNormalization);
        return session;
    }

    private static void SetValue(
        CollectorSessionAggregate session,
        string propertyName,
        object? value)
    {
        var property = typeof(CollectorSessionAggregate).GetProperty(propertyName)
            ?? throw new InvalidOperationException(
                $"Property '{propertyName}' was not found on CollectorSession.");
        property.SetMethod!.Invoke(session, [value]);
    }

    private sealed class Fixture
    {
        public Fixture(
            NormalizationSuitability? suitability = null,
            CollectorSessionAggregate? session = null,
            int projectionVersion = 3)
        {
            Session = session ?? CreateAwaitingNormalizationSession();
            EventEndsAt = Session.EventEndsAt!.Value;
            AwaitingNormalizationAt = Session.AwaitingNormalizationAt ?? EventEndsAt;
            Time = new MutableTimeProvider(EventEndsAt.AddMinutes(1));
            Calls = [];
            Suitability = new SuitabilityReader(suitability ?? FullyProcessed, Calls);
            VersionProvider = new ProjectionVersionProvider(projectionVersion);
            Sessions = new SessionRepository(Session);
            Invalidation = new InvalidationCoordinator(Session, Calls);
            Logger = new TestLogger();
            Coordinator = new CollectorNormalizationSuitabilityCoordinator(
                Sessions,
                Suitability,
                VersionProvider,
                Invalidation,
                Time,
                Logger);
        }

        public static NormalizationSuitability FullyProcessed { get; } = new(
            1250, 1250, 1250, 0, 0, 0, 0, 0, true);

        public static NormalizationSuitability Incomplete { get; } = new(
            1250, 1240, 1240, 10, 0, 0, 0, 0, true);

        public CollectorSessionAggregate Session { get; }
        public DateTimeOffset EventEndsAt { get; }
        public DateTimeOffset AwaitingNormalizationAt { get; }
        public MutableTimeProvider Time { get; }
        public List<string> Calls { get; }
        public SuitabilityReader Suitability { get; }
        public ProjectionVersionProvider VersionProvider { get; }
        public SessionRepository Sessions { get; }
        public InvalidationCoordinator Invalidation { get; }
        public TestLogger Logger { get; }
        public ICollectorNormalizationSuitabilityCoordinator Coordinator { get; }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
    }

    private sealed class SuitabilityReader(
        NormalizationSuitability result,
        List<string> calls) : INormalizationSuitabilityReader
    {
        public List<(CollectorSessionId SessionId, int ProjectionVersion)> Calls { get; } = [];
        public Exception? Exception { get; set; }
        public int CallCount => Calls.Count;

        public Task<NormalizationSuitability> ReadAsync(
            CollectorSessionId sessionId,
            int projectionVersion,
            CancellationToken cancellationToken)
        {
            Calls.Add((sessionId, projectionVersion));
            calls.Add("suitability:read");
            if (Exception is not null)
                throw Exception;
            return Task.FromResult(result);
        }
    }

    private sealed class ProjectionVersionProvider(int projectionVersion)
        : IProjectionVersionProvider
    {
        public int ProjectionVersion { get; } = projectionVersion;
    }

    private sealed class SessionRepository(CollectorSessionAggregate session)
        : ICollectorSessionRepository
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

    private sealed class InvalidationCoordinator(
        CollectorSessionAggregate session,
        List<string> calls) : ICollectorSessionInvalidationCoordinator
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
            calls.Add("invalidation");
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

    private sealed class TestLogger : ILogger<CollectorNormalizationSuitabilityCoordinator>
    {
        public List<(LogLevel Level, Exception? Exception)> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
                Errors.Add((logLevel, exception));
        }
    }
}
