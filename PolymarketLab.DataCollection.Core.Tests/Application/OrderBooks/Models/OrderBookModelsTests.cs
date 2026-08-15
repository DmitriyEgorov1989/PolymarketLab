using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;
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
    public void OrderBookIntegrityIssue_ShouldHaveStableValues()
    {
        ((int)OrderBookIntegrityIssue.CrossedBook).Should().Be(1);
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
