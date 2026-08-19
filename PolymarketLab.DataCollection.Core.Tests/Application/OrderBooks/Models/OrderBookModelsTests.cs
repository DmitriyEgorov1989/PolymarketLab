using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Resynchronization.Models;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.OrderBooks.Models;

public sealed class OrderBookModelsTests
{
    [Fact]
    public void OrderBookSide_ShouldHaveStableValuesWithoutExternalTradeNames()
    {
        ((int)OrderBookSide.Bid).Should().Be(1);
        ((int)OrderBookSide.Ask).Should().Be(2);
        Enum.GetNames<OrderBookSide>().Should().Equal("Bid", "Ask");
    }

    [Fact]
    public void OrderBookSyncStatus_ShouldHaveStableValues()
    {
        ((int)OrderBookSyncStatus.Uninitialized).Should().Be(1);
        ((int)OrderBookSyncStatus.Synchronized).Should().Be(2);
        ((int)OrderBookSyncStatus.Suspect).Should().Be(3);
        ((int)OrderBookSyncStatus.Resynchronizing).Should().Be(4);
        ((int)OrderBookSyncStatus.Stale).Should().Be(5);
    }

    [Fact]
    public void OrderBookIntegrityIssueType_ShouldHaveStableValues()
    {
        ((int)OrderBookIntegrityIssueType.BestBidMismatch).Should().Be(1);
        ((int)OrderBookIntegrityIssueType.BestAskMismatch).Should().Be(2);
        ((int)OrderBookIntegrityIssueType.SpreadMismatch).Should().Be(3);
        ((int)OrderBookIntegrityIssueType.TickSizeMismatch).Should().Be(4);
        ((int)OrderBookIntegrityIssueType.CrossedBook).Should().Be(5);
        ((int)OrderBookIntegrityIssueType.UnexpectedAsset).Should().Be(6);
        ((int)OrderBookIntegrityIssueType.EventOrderViolation).Should().Be(7);
        ((int)OrderBookIntegrityIssueType.GapDetected).Should().Be(8);
        ((int)OrderBookIntegrityIssueType.SnapshotHashMismatch).Should().Be(9);
    }

    [Fact]
    public void OrderBookResynchronizationEnums_ShouldHaveStableValues()
    {
        ((int)OrderBookResyncReason.Manual).Should().Be(1);
        ((int)OrderBookResyncReason.Reconnect).Should().Be(2);
        ((int)OrderBookResyncReason.BestBidMismatch).Should().Be(3);
        ((int)OrderBookResyncReason.BestAskMismatch).Should().Be(4);
        ((int)OrderBookResyncReason.SpreadMismatch).Should().Be(5);
        ((int)OrderBookResyncReason.TickSizeMismatch).Should().Be(6);
        ((int)OrderBookResyncReason.CrossedBook).Should().Be(7);
        ((int)OrderBookResyncReason.GapDetected).Should().Be(8);
        ((int)OrderBookResyncReason.StaleState).Should().Be(9);
        ((int)OrderBookResyncReason.HashMismatch).Should().Be(10);
        ((int)OrderBookResyncOutcome.Synchronized).Should().Be(1);
        ((int)OrderBookResyncOutcome.Failed).Should().Be(2);
    }

    [Fact]
    public void OrderBookIntegrityIssue_ShouldPreserveDiagnosticDetails()
    {
        var detectedAt = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var issue = new OrderBookIntegrityIssue(
            OrderBookIntegrityIssueType.BestBidMismatch,
            "Best bid mismatch.",
            42,
            detectedAt);

        issue.Type.Should().Be(OrderBookIntegrityIssueType.BestBidMismatch);
        issue.Message.Should().Be("Best bid mismatch.");
        issue.NormalizedEventId.Should().Be(42);
        issue.DetectedAt.Should().Be(detectedAt);
    }

    [Fact]
    public void OrderBookLevel_NonNegativeValues_ShouldPreserveState()
    {
        var level = new OrderBookLevel(Price: 0m, Size: 12.5m);

        level.Price.Should().Be(0m);
        level.Size.Should().Be(12.5m);
    }

    [Theory]
    [InlineData("-0.01", "1", "Price")]
    [InlineData("0.5", "-0.01", "Size")]
    public void OrderBookLevel_NegativeValue_ShouldRejectInvalidState(
        string price,
        string size,
        string expectedParameter)
    {
        var action = () => new OrderBookLevel(
            Price: decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture),
            Size: decimal.Parse(size, System.Globalization.CultureInfo.InvariantCulture));

        action.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(expectedParameter);
    }

    [Fact]
    public void NewOrderBookState_ShouldBeUninitialized()
    {
        var state = new OrderBookState("asset");

        state.AssetId.Should().Be("asset");
        state.Status.Should().Be(OrderBookSyncStatus.Uninitialized);
        state.Bids.Should().BeEmpty();
        state.Asks.Should().BeEmpty();
        state.NormalizedEventId.Should().BeNull();
        state.IntegrityIssue.Should().BeNull();
    }
}
