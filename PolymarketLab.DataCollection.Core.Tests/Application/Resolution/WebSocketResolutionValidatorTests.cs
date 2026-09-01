using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.Resolution;
using PolymarketLab.DataCollection.Core.Domain.Models.Resolution;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Tests.Application.Resolution;

public sealed class WebSocketResolutionValidatorTests
{
    private static readonly DateTimeOffset EventStartsAt =
        new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EventEndsAt = EventStartsAt.AddMinutes(5);
    private readonly WebSocketResolutionValidator _validator = new();

    [Fact]
    public void Validate_WithMatchingCurrentEpochObservation_ShouldReturnWinner()
    {
        var result = _validator.Validate(
            CreateCandidate(),
            CreateSession(),
            currentConnectionEpoch: 3,
            confirmationDeadline: EventEndsAt.AddMinutes(5));

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(WebSocketResolutionObservationStatus.Terminal);
        result.Value.Winner.Should().Be(new ResolutionWinner("1001", "Yes"));
    }

    [Fact]
    public void Validate_WithPreEndObservation_ShouldRejectWithoutConflict()
    {
        var result = _validator.Validate(
            CreateCandidate(receivedAt: EventEndsAt.AddTicks(-1)),
            CreateSession(),
            currentConnectionEpoch: 3,
            confirmationDeadline: EventEndsAt.AddMinutes(5));

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(WebSocketResolutionObservationStatus.Rejected);
        result.Value.RejectionCode.Should().Be("PreEndObservation");
    }

    [Fact]
    public void Validate_WithStaleEpoch_ShouldRejectWithoutConflict()
    {
        var result = _validator.Validate(
            CreateCandidate(connectionEpoch: 2),
            CreateSession(),
            currentConnectionEpoch: 3,
            confirmationDeadline: EventEndsAt.AddMinutes(5));

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(WebSocketResolutionObservationStatus.Rejected);
        result.Value.RejectionCode.Should().Be("StaleConnectionEpoch");
    }

    [Fact]
    public void Validate_WithPostDeadlineObservation_ShouldRejectWithoutConflict()
    {
        var deadline = EventEndsAt.AddMinutes(5);

        var result = _validator.Validate(
            CreateCandidate(receivedAt: deadline.AddTicks(1), conditionId: "0xwrong"),
            CreateSession(),
            currentConnectionEpoch: 3,
            confirmationDeadline: deadline);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(WebSocketResolutionObservationStatus.Rejected);
        result.Value.RejectionCode.Should().Be("PostDeadlineObservation");
    }

    [Fact]
    public void Validate_WithWrongCondition_ShouldReturnConflict()
    {
        var result = _validator.Validate(
            CreateCandidate(conditionId: "0xwrong"),
            CreateSession(),
            currentConnectionEpoch: 3,
            confirmationDeadline: EventEndsAt.AddMinutes(5));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.resolution.conflict");
    }

    [Fact]
    public void Validate_WithWrongTokenSet_ShouldReturnConflict()
    {
        var result = _validator.Validate(
            CreateCandidate(assetIds: ["1001", "9999"]),
            CreateSession(),
            currentConnectionEpoch: 3,
            confirmationDeadline: EventEndsAt.AddMinutes(5));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.resolution.conflict");
    }

    [Fact]
    public void Validate_WithMismatchedWinnerOutcome_ShouldReturnConflict()
    {
        var result = _validator.Validate(
            CreateCandidate(winningOutcome: "No"),
            CreateSession(),
            currentConnectionEpoch: 3,
            confirmationDeadline: EventEndsAt.AddMinutes(5));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.resolution.conflict");
    }

    private static WebSocketResolutionCandidate CreateCandidate(
        long connectionEpoch = 3,
        DateTimeOffset? receivedAt = null,
        string conditionId = "0xabc",
        IReadOnlyCollection<string>? assetIds = null,
        string winningOutcome = "Yes") =>
        new(
            RawMessageId: 42,
            RawItemIndex: 1,
            ConnectionEpoch: connectionEpoch,
            ReceivedAt: receivedAt ?? EventEndsAt,
            ExternalMarketId: "market-123",
            ConditionId: conditionId,
            AssetIds: assetIds ?? ["1001", "1002"],
            WinningAssetId: "1001",
            WinningOutcome: winningOutcome);

    private static CollectorSessionAggregate CreateSession() =>
        CollectorSessionAggregate.Create(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            MarketId.Create(Guid.NewGuid()).Value,
            "event-123",
            "event-slug",
            "market-123",
            "market-slug",
            "0xabc",
            EventStartsAt,
            EventEndsAt,
            1,
            [
                new CollectorSessionTokenDefinition(TokenId.Create("1001").Value, "Yes", 0),
                new CollectorSessionTokenDefinition(TokenId.Create("1002").Value, "No", 1)
            ],
            EventStartsAt.AddMinutes(-2)).Value;
}
