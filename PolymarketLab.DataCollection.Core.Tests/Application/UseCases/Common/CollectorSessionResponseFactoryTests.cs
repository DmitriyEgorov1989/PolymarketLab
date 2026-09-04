using CSharpFunctionalExtensions;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.UseCases.Common;
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

namespace PolymarketLab.DataCollection.Core.Tests.Application.UseCases.Common;

public sealed class CollectorSessionResponseFactoryTests
{
    private static readonly DateTimeOffset CreatedAt =
        DateTimeOffset.Parse("2026-09-04T11:57:00Z");
    private static readonly DateTimeOffset EventStartsAt =
        DateTimeOffset.Parse("2026-09-04T12:00:00Z");
    private static readonly DateTimeOffset EventEndsAt =
        DateTimeOffset.Parse("2026-09-04T12:05:00Z");

    [Fact]
    public async Task CreateAsync_WithStoppedSession_ShouldMapFullEvidenceSnapshot()
    {
        var session = CreateSession();
        var startedAt = CreatedAt.AddMinutes(2);
        CollectorSessionTestFactory.MarkRunning(session, startedAt);
        session.MarkCollectingWindow().IsSuccess.Should().BeTrue();
        session.MarkAwaitingResolution().IsSuccess.Should().BeTrue();
        var signaledAt = EventEndsAt.AddSeconds(1);
        var confirmedAt = EventEndsAt.AddSeconds(3);
        session.ConfirmResolution(
                signaledAt,
                confirmedAt,
                new ResolutionWinner("1001", "Yes"),
                2)
            .IsSuccess.Should().BeTrue();
        session.MarkStopping().IsSuccess.Should().BeTrue();
        var awaitingNormalizationAt = EventEndsAt.AddSeconds(4);
        session.MarkAwaitingNormalization(awaitingNormalizationAt)
            .IsSuccess.Should().BeTrue();
        var stoppedAt = EventEndsAt.AddSeconds(5);
        session.Stop(stoppedAt, CollectorStopReason.MarketClosed)
            .IsSuccess.Should().BeTrue();

        var lastMessageAt = EventEndsAt.AddSeconds(3);
        var progress = new CollectorSessionProgress(
            session.Id,
            2,
            1250,
            1250,
            1250,
            1250,
            lastMessageAt,
            1);
        var readiness = new[]
        {
            new CollectorTokenReadiness(
                session.Id,
                2,
                TokenId.Create("1001").Value,
                DateTimeOffset.Parse("2026-09-04T11:59:44Z")),
            new CollectorTokenReadiness(
                session.Id,
                2,
                TokenId.Create("1002").Value,
                DateTimeOffset.Parse("2026-09-04T11:59:45Z"))
        };
        var normalization = new NormalizationSuitability(
            1250, 1250, 1240, 10, 0, 0, 0, 0, true);
        var resolution = new DurableResolutionState(
            session.Id,
            10,
            EventEndsAt.AddSeconds(2),
            new ResolutionConfirmationReference(2, 3, confirmedAt),
            [
                Observation(1, ResolutionObservationSource.WebSocket,
                    DurableResolutionObservationStatus.Terminal, signaledAt,
                    new ResolutionWinner("1001", "Yes"), 2),
                Observation(2, ResolutionObservationSource.Gamma,
                    DurableResolutionObservationStatus.Terminal, EventEndsAt.AddSeconds(2),
                    new ResolutionWinner("1001", "Yes")),
                Observation(3, ResolutionObservationSource.Clob,
                    DurableResolutionObservationStatus.Terminal, EventEndsAt.AddSeconds(3),
                    new ResolutionWinner("1001", "Yes"))
            ]);
        var factory = new CollectorSessionResponseFactory(
            new StubProgressRepository(progress),
            new StubCollectorTokenReadinessRepository(readiness),
            new StubResolutionObservationRepository(resolution),
            new StubCollectorDatasetCleanupAuditReader(null),
            new StubNormalizationSuitabilityReader(normalization));

        var response = await factory.CreateAsync(session, CancellationToken.None);

        response.Should().BeEquivalentTo(new
        {
            SessionId = session.Id.Value,
            MarketId = session.MarketId.Value,
            Status = "Stopped",
            Phase = (string?)null,
            EffectiveDeadline = (DateTimeOffset?)null,
            CreatedAt,
            StartedAt = (DateTimeOffset?)startedAt,
            SubscriptionReadyAt = (DateTimeOffset?)startedAt,
            StoppedAt = (DateTimeOffset?)stoppedAt,
            InvalidatingAt = (DateTimeOffset?)null,
            StopReason = "MarketClosed",
            FailureCode = (string?)null,
            FailureMessage = (string?)null,
            MessagesReceived = 1250L,
            MessagesEnqueued = 1250L,
            MessagesPersisted = 1250L,
            RemainingRawMessageCount = 1250L,
            LastMessageAt = (DateTimeOffset?)lastMessageAt,
            ReconnectCount = 1L
        });
        response.Snapshot.Should().BeEquivalentTo(new
        {
            ExternalEventId = "event-123",
            EventSlug = "btc-updown-5m-1200",
            ExternalMarketId = "market-123",
            MarketSlug = "btc-updown-5m-1200",
            ConditionId = "0xabc",
            EventStartsAt = (DateTimeOffset?)EventStartsAt,
            EventEndsAt = (DateTimeOffset?)EventEndsAt,
            ProjectionVersion = (int?)3
        });
        response.Snapshot.Tokens.Should().Equal(
            new CollectorSessionTokenResponse("1001", "Yes", 0),
            new CollectorSessionTokenResponse("1002", "No", 1));
        response.Readiness.ConnectionEpoch.Should().Be(2);
        response.Readiness.Tokens.Should().Equal(
            new CollectorTokenReadinessResponse(
                "1001",
                DateTimeOffset.Parse("2026-09-04T11:59:44Z")),
            new CollectorTokenReadinessResponse(
                "1002",
                DateTimeOffset.Parse("2026-09-04T11:59:45Z")));
        response.Normalization.Should().BeEquivalentTo(new
        {
            RawCount = 1250L,
            LedgerCount = 1250L,
            ProcessedCount = 1240L,
            PendingCount = 10L,
            ProcessingCount = 0L,
            UnsupportedCount = 0L,
            InvalidCount = 0L,
            FailedCount = 0L,
            MissingCount = 0L,
            ResolutionRawItemProcessed = true
        });
        response.Resolution.SignaledAt.Should().Be(signaledAt);
        response.Resolution.ConfirmedAt.Should().Be(confirmedAt);
        response.Resolution.WinningTokenId.Should().Be("1001");
        response.Resolution.WinningOutcome.Should().Be("Yes");
        response.Resolution.ConnectionEpoch.Should().Be(2);
        response.Resolution.LastPollingCycleAt.Should().Be(EventEndsAt.AddSeconds(2));
        response.Resolution.SourceStates.Should().Equal(
            Source("WebSocket", "Terminal", signaledAt, "1001", "Yes"),
            Source("Gamma", "Terminal", EventEndsAt.AddSeconds(2), "1001", "Yes"),
            Source("Clob", "Terminal", EventEndsAt.AddSeconds(3), "1001", "Yes"));
        response.Resolution.ConfirmationSources.Should().Equal(
            Source("WebSocket", "Terminal", signaledAt, "1001", "Yes"),
            Source("Gamma", "Terminal", EventEndsAt.AddSeconds(2), "1001", "Yes"),
            Source("Clob", "Terminal", EventEndsAt.AddSeconds(3), "1001", "Yes"));
        response.Cleanup.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_WithStoppingAwaitingNormalization_ShouldMapDynamicDeadline()
    {
        var session = CreateSession();
        var startedAt = CreatedAt.AddMinutes(2);
        CollectorSessionTestFactory.MarkRunning(session, startedAt);
        session.MarkCollectingWindow().IsSuccess.Should().BeTrue();
        session.MarkAwaitingResolution().IsSuccess.Should().BeTrue();
        session.ConfirmResolution(
                EventEndsAt.AddSeconds(1),
                EventEndsAt.AddSeconds(3),
                new ResolutionWinner("1001", "Yes"),
                1)
            .IsSuccess.Should().BeTrue();
        session.MarkStopping().IsSuccess.Should().BeTrue();
        var awaitingNormalizationAt = EventEndsAt.AddSeconds(4);
        session.MarkAwaitingNormalization(awaitingNormalizationAt)
            .IsSuccess.Should().BeTrue();

        var progress = new CollectorSessionProgress(session.Id, 1, 10, 10, 10, 10, null, 0);
        var normalization = new NormalizationSuitability(10, 10, 9, 1, 0, 0, 0, 0, false);
        var factory = new CollectorSessionResponseFactory(
            new StubProgressRepository(progress),
            new StubCollectorTokenReadinessRepository(),
            new StubResolutionObservationRepository(EmptyResolution(session.Id)),
            new StubCollectorDatasetCleanupAuditReader(null),
            new StubNormalizationSuitabilityReader(normalization));

        var response = await factory.CreateAsync(session, CancellationToken.None);

        response.Status.Should().Be("Stopping");
        response.Phase.Should().Be("AwaitingNormalization");
        response.EffectiveDeadline.Should().Be(awaitingNormalizationAt.AddMinutes(5));
        response.Normalization!.PendingCount.Should().Be(1);
        response.Cleanup.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_WithCleanedFailedSession_ShouldKeepHistoryAndOmitNormalization()
    {
        var session = CreateSession();
        var startedAt = CreatedAt.AddMinutes(2);
        CollectorSessionTestFactory.MarkRunning(session, startedAt);
        session.Fail(
            startedAt.AddMinutes(3),
            CollectorStopReason.PersistenceFailure,
            "collector.runtime.persist.failed",
            "Persistence failed.");
        var invalidatingAt = startedAt.AddMinutes(2);
        var sessionId = session.Id;
        var cleanup = new CollectorDatasetCleanupAudit(
            sessionId,
            invalidatingAt.AddMinutes(1),
            1250,
            1250,
            3);
        var progress = new CollectorSessionProgress(
            sessionId,
            1,
            1250,
            1250,
            1250,
            0,
            startedAt.AddMinutes(3),
            0);
        var factory = new CollectorSessionResponseFactory(
            new StubProgressRepository(progress),
            new StubCollectorTokenReadinessRepository(),
            new StubResolutionObservationRepository(EmptyResolution(sessionId)),
            new StubCollectorDatasetCleanupAuditReader(cleanup),
            new StubNormalizationSuitabilityReader(null));

        var response = await factory.CreateAsync(session, CancellationToken.None);

        response.Status.Should().Be("Failed");
        response.MessagesReceived.Should().Be(1250);
        response.MessagesPersisted.Should().Be(1250);
        response.RemainingRawMessageCount.Should().Be(0);
        response.Normalization.Should().BeNull();
        response.Cleanup.Should().NotBeNull();
        response.Cleanup!.CleanedAt.Should().Be(invalidatingAt.AddMinutes(1));
        response.Cleanup.ProjectionVersion.Should().Be(3);
        response.Cleanup.FailureCode.Should().Be("collector.runtime.persist.failed");
        response.Cleanup.FailureMessage.Should().Be("Persistence failed.");
        response.Cleanup.DeletedRawMessageCount.Should().Be(1250);
        response.Cleanup.DeletedNormalizationCount.Should().Be(1250);
        response.Cleanup.DeletedNormalizedEventCount.Should().Be(3);
    }

    [Fact]
    public async Task CreateAsync_WithInterruptedSession_ShouldMapTerminalShape()
    {
        var session = CreateSession();
        var startedAt = CreatedAt.AddMinutes(2);
        CollectorSessionTestFactory.MarkRunning(session, startedAt);
        session.Interrupt(EventEndsAt.AddMinutes(1), CollectorStopReason.ProcessTerminated)
            .IsSuccess.Should().BeTrue();

        var factory = new CollectorSessionResponseFactory(
            new StubProgressRepository(CollectorSessionProgress.Empty(session.Id)),
            new StubCollectorTokenReadinessRepository(),
            new StubResolutionObservationRepository(EmptyResolution(session.Id)),
            new StubCollectorDatasetCleanupAuditReader(null),
            new StubNormalizationSuitabilityReader());

        var response = await factory.CreateAsync(session, CancellationToken.None);

        response.Status.Should().Be("Interrupted");
        response.Phase.Should().BeNull();
        response.EffectiveDeadline.Should().BeNull();
        response.StopReason.Should().Be("ProcessTerminated");
        response.Readiness.ConnectionEpoch.Should().Be(0);
        response.Readiness.Tokens.Should().HaveCount(2);
        response.Readiness.Tokens.All(token => token.InitialBookEnqueuedAt is null)
            .Should().BeTrue();
        response.Resolution.SourceStates.Should().BeEmpty();
        response.Resolution.ConfirmationSources.Should().BeEmpty();
        response.Cleanup.Should().BeNull();
    }

    [Theory]
    [InlineData(-70, -10)]
    [InlineData(-10, 0)]
    [InlineData(0, 0)]
    public async Task CreateAsync_WithConnectingPhase_ShouldMapReadinessDeadline(
        int startedAtOffsetSeconds,
        int deadlineOffsetSeconds)
    {
        var session = CreateSession();
        session.BeginPreparation(EventStartsAt.AddSeconds(startedAtOffsetSeconds))
            .IsSuccess.Should().BeTrue();

        var factory = new CollectorSessionResponseFactory(
            new StubProgressRepository(CollectorSessionProgress.Empty(session.Id)),
            new StubCollectorTokenReadinessRepository(),
            new StubResolutionObservationRepository(EmptyResolution(session.Id)),
            new StubCollectorDatasetCleanupAuditReader(null),
            new StubNormalizationSuitabilityReader());

        var response = await factory.CreateAsync(session, CancellationToken.None);

        response.Phase.Should().Be("Connecting");
        response.EffectiveDeadline.Should().Be(
            EventStartsAt.AddSeconds(deadlineOffsetSeconds));
    }

    [Fact]
    public async Task CreateAsync_WithLateNonTerminalObservation_ShouldKeepConfirmationEvidence()
    {
        var session = CreateSession();
        CollectorSessionTestFactory.MarkRunning(session, CreatedAt.AddMinutes(2));
        session.MarkCollectingWindow().IsSuccess.Should().BeTrue();
        session.MarkAwaitingResolution().IsSuccess.Should().BeTrue();
        var signaledAt = EventEndsAt.AddSeconds(1);
        var confirmedAt = EventEndsAt.AddSeconds(2);
        session.ConfirmResolution(
                signaledAt,
                confirmedAt,
                new ResolutionWinner("1001", "Yes"),
                1)
            .IsSuccess.Should().BeTrue();

        var resolution = new DurableResolutionState(
            session.Id,
            10,
            EventEndsAt.AddSeconds(4),
            new ResolutionConfirmationReference(2, 3, confirmedAt),
            [
                Observation(1, ResolutionObservationSource.WebSocket,
                    DurableResolutionObservationStatus.Terminal, signaledAt,
                    new ResolutionWinner("1001", "Yes"), 1),
                Observation(2, ResolutionObservationSource.Gamma,
                    DurableResolutionObservationStatus.Terminal, EventEndsAt.AddSeconds(2),
                    new ResolutionWinner("1001", "Yes")),
                Observation(3, ResolutionObservationSource.Clob,
                    DurableResolutionObservationStatus.Terminal, EventEndsAt.AddSeconds(3),
                    new ResolutionWinner("1001", "Yes")),
                Observation(4, ResolutionObservationSource.Clob,
                    DurableResolutionObservationStatus.NonTerminal, EventEndsAt.AddSeconds(4),
                    null)
            ]);
        var factory = new CollectorSessionResponseFactory(
            new StubProgressRepository(new CollectorSessionProgress(session.Id, 1, 0, 0, 0, 0, null, 0)),
            new StubCollectorTokenReadinessRepository(),
            new StubResolutionObservationRepository(resolution),
            new StubCollectorDatasetCleanupAuditReader(null),
            new StubNormalizationSuitabilityReader(null));

        var response = await factory.CreateAsync(session, CancellationToken.None);

        response.Resolution.SourceStates.Should().Equal(
            Source("WebSocket", "Terminal", signaledAt, "1001", "Yes"),
            Source("Gamma", "Terminal", EventEndsAt.AddSeconds(2), "1001", "Yes"),
            Source("Clob", "NonTerminal", EventEndsAt.AddSeconds(4), null, null));
        response.Resolution.ConfirmationSources.Should().Equal(
            Source("WebSocket", "Terminal", signaledAt, "1001", "Yes"),
            Source("Gamma", "Terminal", EventEndsAt.AddSeconds(2), "1001", "Yes"),
            Source("Clob", "Terminal", EventEndsAt.AddSeconds(3), "1001", "Yes"));
    }

    [Fact]
    public async Task CreateAsync_WithNewerTimestampAndLowerId_ShouldPickObservationByTimestamp()
    {
        var session = CreateSession();
        var resolution = new DurableResolutionState(
            session.Id,
            0,
            null,
            null,
            [
                Observation(5, ResolutionObservationSource.Clob,
                    DurableResolutionObservationStatus.Terminal, EventEndsAt.AddSeconds(3),
                    new ResolutionWinner("1001", "Yes")),
                Observation(2, ResolutionObservationSource.Clob,
                    DurableResolutionObservationStatus.NonTerminal, EventEndsAt.AddSeconds(4),
                    null)
            ]);
        var factory = CreateEmptyFactory(resolution);

        var response = await factory.CreateAsync(session, CancellationToken.None);

        response.Resolution.SourceStates.Should().ContainSingle().Which.Should()
            .Be(Source("Clob", "NonTerminal", EventEndsAt.AddSeconds(4), null, null));
    }

    [Fact]
    public async Task CreateAsync_WithEqualTimestamps_ShouldPickHighestObservationId()
    {
        var session = CreateSession();
        var resolution = new DurableResolutionState(
            session.Id,
            0,
            null,
            null,
            [
                Observation(3, ResolutionObservationSource.Clob,
                    DurableResolutionObservationStatus.Terminal, EventEndsAt.AddSeconds(3),
                    new ResolutionWinner("1001", "Yes")),
                Observation(4, ResolutionObservationSource.Clob,
                    DurableResolutionObservationStatus.NonTerminal, EventEndsAt.AddSeconds(3),
                    null)
            ]);
        var factory = CreateEmptyFactory(resolution);

        var response = await factory.CreateAsync(session, CancellationToken.None);

        response.Resolution.SourceStates.Should().ContainSingle().Which.Should()
            .Be(Source("Clob", "NonTerminal", EventEndsAt.AddSeconds(3), null, null));
    }

    [Theory]
    [InlineData(CollectorSessionPhase.WaitingForPreparation)]
    [InlineData(CollectorSessionPhase.Connecting)]
    [InlineData(CollectorSessionPhase.AwaitingInitialBooks)]
    [InlineData(CollectorSessionPhase.AwaitingHeartbeat)]
    [InlineData(CollectorSessionPhase.ReadyBeforeWindow)]
    [InlineData(CollectorSessionPhase.CollectingWindow)]
    [InlineData(CollectorSessionPhase.AwaitingResolution)]
    [InlineData(CollectorSessionPhase.DrainingRaw)]
    [InlineData(CollectorSessionPhase.AwaitingNormalization)]
    [InlineData(CollectorSessionPhase.Cleaning)]
    public async Task CreateAsync_WithPhase_ShouldMapEffectiveDeadline(CollectorSessionPhase phase)
    {
        var (session, expected) = CreateSessionInPhase(phase);

        var factory = new CollectorSessionResponseFactory(
            new StubProgressRepository(CollectorSessionProgress.Empty(session.Id)),
            new StubCollectorTokenReadinessRepository(),
            new StubResolutionObservationRepository(EmptyResolution(session.Id)),
            new StubCollectorDatasetCleanupAuditReader(null),
            new StubNormalizationSuitabilityReader(null));

        var response = await factory.CreateAsync(session, CancellationToken.None);

        response.Phase.Should().Be(phase.ToString());
        response.EffectiveDeadline.Should().Be(expected);
    }

    private (CollectorSessionAggregate Session, DateTimeOffset? Deadline) CreateSessionInPhase(
        CollectorSessionPhase phase)
    {
        var session = CreateSession();
        var startedAt = CreatedAt.AddMinutes(2);
        CollectorSessionTestFactory.MarkRunning(session, startedAt);

        switch (phase)
        {
            case CollectorSessionPhase.WaitingForPreparation:
                var scheduled = CreateSession();
                return (scheduled, EventStartsAt.AddSeconds(-60));
            case CollectorSessionPhase.Connecting:
                var connecting = CreateSession();
                connecting.BeginPreparation(startedAt).IsSuccess.Should().BeTrue();
                return (connecting, EventStartsAt.AddSeconds(-10));
            case CollectorSessionPhase.AwaitingInitialBooks:
                var initialBooks = CreateSession();
                initialBooks.BeginPreparation(startedAt).IsSuccess.Should().BeTrue();
                initialBooks.MarkAwaitingInitialBooks().IsSuccess.Should().BeTrue();
                return (initialBooks, EventStartsAt.AddSeconds(-10));
            case CollectorSessionPhase.AwaitingHeartbeat:
                var heartbeat = CreateSession();
                heartbeat.BeginPreparation(startedAt).IsSuccess.Should().BeTrue();
                heartbeat.MarkAwaitingInitialBooks().IsSuccess.Should().BeTrue();
                heartbeat.MarkAwaitingHeartbeat().IsSuccess.Should().BeTrue();
                return (heartbeat, EventStartsAt.AddSeconds(-10));
            case CollectorSessionPhase.ReadyBeforeWindow:
                return (session, EventStartsAt);
            case CollectorSessionPhase.CollectingWindow:
                session.MarkCollectingWindow().IsSuccess.Should().BeTrue();
                return (session, EventEndsAt);
            case CollectorSessionPhase.AwaitingResolution:
                session.MarkCollectingWindow().IsSuccess.Should().BeTrue();
                session.MarkAwaitingResolution().IsSuccess.Should().BeTrue();
                return (session, EventEndsAt.AddMinutes(5));
            case CollectorSessionPhase.DrainingRaw:
                session.MarkCollectingWindow().IsSuccess.Should().BeTrue();
                session.MarkAwaitingResolution().IsSuccess.Should().BeTrue();
                session.ConfirmResolution(
                        EventEndsAt.AddSeconds(1),
                        EventEndsAt.AddSeconds(2),
                        new ResolutionWinner("1001", "Yes"),
                        1)
                    .IsSuccess.Should().BeTrue();
                session.MarkStopping().IsSuccess.Should().BeTrue();
                return (session, null);
            case CollectorSessionPhase.AwaitingNormalization:
                session.MarkCollectingWindow().IsSuccess.Should().BeTrue();
                session.MarkAwaitingResolution().IsSuccess.Should().BeTrue();
                session.ConfirmResolution(
                        EventEndsAt.AddSeconds(1),
                        EventEndsAt.AddSeconds(2),
                        new ResolutionWinner("1001", "Yes"),
                        1)
                    .IsSuccess.Should().BeTrue();
                session.MarkStopping().IsSuccess.Should().BeTrue();
                var awaitingNormalizationAt = EventEndsAt.AddSeconds(4);
                session.MarkAwaitingNormalization(awaitingNormalizationAt)
                    .IsSuccess.Should().BeTrue();
                return (session, awaitingNormalizationAt.AddMinutes(5));
            case CollectorSessionPhase.Cleaning:
                session.MarkCollectingWindow().IsSuccess.Should().BeTrue();
                session.MarkAwaitingResolution().IsSuccess.Should().BeTrue();
                session.BeginInvalidation(
                        EventEndsAt.AddSeconds(3),
                        CollectorStopReason.PersistenceFailure,
                        "collector.test.failure",
                        "Failure.")
                    .IsSuccess.Should().BeTrue();
                return (session, null);
            default:
                throw new ArgumentOutOfRangeException(nameof(phase), phase, null);
        }
    }

    private static CollectorSessionAggregate CreateSession() =>
        CollectorSessionTestFactory.CreateScheduled(createdAt: CreatedAt);

    private static CollectorSessionResponseFactory CreateEmptyFactory(
        DurableResolutionState resolution) =>
        new(
            new StubProgressRepository(CollectorSessionProgress.Empty(
                resolution.SessionId)),
            new StubCollectorTokenReadinessRepository(),
            new StubResolutionObservationRepository(resolution),
            new StubCollectorDatasetCleanupAuditReader(null),
            new StubNormalizationSuitabilityReader());

    private static DurableResolutionState EmptyResolution(CollectorSessionId sessionId) =>
        new(sessionId, 0, null, null, []);

    private static DurableResolutionObservation Observation(
        long id,
        ResolutionObservationSource source,
        DurableResolutionObservationStatus status,
        DateTimeOffset observedAt,
        ResolutionWinner? winner,
        long? connectionEpoch = null) =>
        new(
            id,
            source,
            observedAt,
            status,
            winner,
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
            null,
            null,
            null,
            connectionEpoch,
            []);

    private static CollectorResolutionSourceResponse Source(
        string source,
        string status,
        DateTimeOffset observedAt,
        string? winningTokenId,
        string? winningOutcome) =>
        new(source, status, observedAt, winningTokenId, winningOutcome, null, null);

    private sealed class StubProgressRepository(CollectorSessionProgress progress)
        : ICollectorSessionProgressRepository
    {
        public Task<CollectorSessionProgress> GetAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken) => Task.FromResult(progress);

        public Task CheckpointAsync(
            CollectorSessionProgressCheckpoint checkpoint,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
