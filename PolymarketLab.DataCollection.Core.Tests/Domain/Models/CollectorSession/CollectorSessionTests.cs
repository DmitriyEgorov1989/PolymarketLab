using FluentAssertions;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Tests.Domain.Models.CollectorSession;

public sealed class CollectorSessionTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 27, 11, 57, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EventStartsAt =
        new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EventEndsAt =
        new(2026, 8, 27, 12, 5, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WithVerifiedSnapshot_ShouldCreateScheduledSession()
    {
        var result = CreateSessionResult();

        result.IsSuccess.Should().BeTrue();
        var session = result.Value;
        session.Status.Should().Be(CollectorSessionStatus.Scheduled);
        session.Phase.Should().Be(CollectorSessionPhase.WaitingForPreparation);
        session.ExternalEventId.Should().Be("event-123");
        session.EventSlug.Should().Be("btc-updown-5m-1200");
        session.ExternalMarketId.Should().Be("market-123");
        session.MarketSlug.Should().Be("btc-updown-5m-1200");
        session.ConditionId.Should().Be("0xabc");
        session.EventStartsAt.Should().Be(EventStartsAt);
        session.EventEndsAt.Should().Be(EventEndsAt);
        session.ProjectionVersion.Should().Be(3);
        session.Tokens.Select(token => (token.TokenId.Value, token.Outcome, token.OutcomeIndex))
            .Should()
            .Equal(("1001", "Yes", 0), ("1002", "No", 1));
        session.StartedAt.Should().BeNull();
        session.SubscriptionReadyAt.Should().BeNull();
    }

    [Fact]
    public void Create_ShouldCopyTokenSnapshot()
    {
        var tokens = CreateTokenDefinitions().ToList();
        var result = CreateSessionResult(tokens);

        tokens.Clear();

        result.Value.Tokens.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithInvalidProjectionVersion_ShouldReturnError(int projectionVersion)
    {
        var result = CreateSessionResult(projectionVersion: projectionVersion);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.session.projection_version.invalid");
    }

    [Fact]
    public void Create_WithInvalidWindow_ShouldReturnError()
    {
        var result = CreateSessionResult(eventEndsAt: EventStartsAt);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.session.window.invalid");
    }

    [Fact]
    public void Create_WithDuplicateTokenId_ShouldReturnError()
    {
        var result = CreateSessionResult(
            [
                new CollectorSessionTokenDefinition(TokenId.Create("1001").Value, "Yes", 0),
                new CollectorSessionTokenDefinition(TokenId.Create("1001").Value, "No", 1)
            ]);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.session.token_id.duplicate");
    }

    [Fact]
    public void PreparationAndReadiness_ShouldKeepSeparateTimestamps()
    {
        var session = CreateSession();
        var preparationStartedAt = CreatedAt.AddMinutes(2);
        var subscriptionReadyAt = preparationStartedAt.AddSeconds(20);

        session.BeginPreparation(preparationStartedAt).IsSuccess.Should().BeTrue();
        session.MarkAwaitingInitialBooks().IsSuccess.Should().BeTrue();
        session.MarkAwaitingHeartbeat().IsSuccess.Should().BeTrue();
        session.MarkRunning(subscriptionReadyAt).IsSuccess.Should().BeTrue();

        session.Status.Should().Be(CollectorSessionStatus.Running);
        session.Phase.Should().Be(CollectorSessionPhase.ReadyBeforeWindow);
        session.StartedAt.Should().Be(preparationStartedAt);
        session.SubscriptionReadyAt.Should().Be(subscriptionReadyAt);
    }

    [Fact]
    public void FullSuccessfulLifecycle_ShouldUseExactStatusPhasePairs()
    {
        var session = CreateRunningSession();

        session.MarkCollectingWindow().IsSuccess.Should().BeTrue();
        session.Phase.Should().Be(CollectorSessionPhase.CollectingWindow);
        session.MarkAwaitingResolution().IsSuccess.Should().BeTrue();
        session.Phase.Should().Be(CollectorSessionPhase.AwaitingResolution);
        session.MarkStopping().IsSuccess.Should().BeTrue();
        session.Status.Should().Be(CollectorSessionStatus.Stopping);
        session.Phase.Should().Be(CollectorSessionPhase.DrainingRaw);
        session.MarkAwaitingNormalization().IsSuccess.Should().BeTrue();
        session.Phase.Should().Be(CollectorSessionPhase.AwaitingNormalization);
        session.Stop(EventEndsAt, CollectorStopReason.MarketClosed)
            .IsSuccess.Should().BeTrue();
        session.Status.Should().Be(CollectorSessionStatus.Stopped);
        session.Phase.Should().BeNull();
    }

    [Fact]
    public void Stop_WithRunningSession_ShouldClearPhase()
    {
        var session = CreateRunningSession();

        var result = session.Stop(EventEndsAt, CollectorStopReason.Requested);

        result.IsSuccess.Should().BeTrue();
        session.Status.Should().Be(CollectorSessionStatus.Stopped);
        session.Phase.Should().BeNull();
    }

    [Fact]
    public void BeginInvalidation_WithScheduledSession_ShouldSetCleaningPhase()
    {
        var session = CreateSession();

        var result = session.BeginInvalidation();

        result.IsSuccess.Should().BeTrue();
        session.Status.Should().Be(CollectorSessionStatus.Invalidating);
        session.Phase.Should().Be(CollectorSessionPhase.Cleaning);
    }

    [Fact]
    public void MarkAwaitingInitialBooks_WithoutPreparation_ShouldReturnError()
    {
        var session = CreateSession();

        var result = session.MarkAwaitingInitialBooks();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.session.phase_transition.invalid");
        session.Status.Should().Be(CollectorSessionStatus.Scheduled);
        session.Phase.Should().Be(CollectorSessionPhase.WaitingForPreparation);
    }

    [Fact]
    public void Interrupt_WithTimeBeforeStart_ShouldReturnError()
    {
        var session = CreateRunningSession();

        var result = session.Interrupt(
            CreatedAt,
            CollectorStopReason.ProcessTerminated);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.session.stopped_at.invalid");
        session.Status.Should().Be(CollectorSessionStatus.Running);
    }

    private static CollectorSessionAggregate CreateRunningSession()
    {
        var session = CreateSession();
        session.BeginPreparation(CreatedAt.AddMinutes(2));
        session.MarkAwaitingInitialBooks();
        session.MarkAwaitingHeartbeat();
        session.MarkRunning(CreatedAt.AddMinutes(2).AddSeconds(20));
        return session;
    }

    private static CollectorSessionAggregate CreateSession() =>
        CreateSessionResult().Value;

    private static CSharpFunctionalExtensions.Result<CollectorSessionAggregate, PolymarketLab.SharedKernel.Errors.Error>
        CreateSessionResult(
            IReadOnlyCollection<CollectorSessionTokenDefinition>? tokens = null,
            int projectionVersion = 3,
            DateTimeOffset? eventEndsAt = null)
    {
        return CollectorSessionAggregate.Create(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            MarketId.Create(Guid.NewGuid()).Value,
            "event-123",
            "btc-updown-5m-1200",
            "market-123",
            "btc-updown-5m-1200",
            "0xabc",
            EventStartsAt,
            eventEndsAt ?? EventEndsAt,
            projectionVersion,
            tokens ?? CreateTokenDefinitions(),
            CreatedAt);
    }

    private static IReadOnlyCollection<CollectorSessionTokenDefinition> CreateTokenDefinitions() =>
    [
        new CollectorSessionTokenDefinition(TokenId.Create("1001").Value, "Yes", 0),
        new CollectorSessionTokenDefinition(TokenId.Create("1002").Value, "No", 1)
    ];
}
