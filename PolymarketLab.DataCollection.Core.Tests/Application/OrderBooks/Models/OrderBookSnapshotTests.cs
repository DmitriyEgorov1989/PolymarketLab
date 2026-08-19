using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.OrderBooks.Models;

public sealed class OrderBookSnapshotTests
{
    [Fact]
    public void Constructor_ShouldPreserveValuesAndOwnLevelCollections()
    {
        var bid = new OrderBookSnapshotLevel(0.4m, 10m);
        var ask = new OrderBookSnapshotLevel(0.6m, 20m);
        var bids = new List<OrderBookSnapshotLevel> { bid };
        var asks = new List<OrderBookSnapshotLevel> { ask };

        var snapshot = new OrderBookSnapshot(
            "condition",
            "asset",
            1_765_000_000_123,
            "hash",
            bids,
            asks,
            1m,
            0.01m,
            true,
            0.5m);
        bids.Clear();
        asks.Clear();

        snapshot.MarketConditionId.Should().Be("condition");
        snapshot.AssetId.Should().Be("asset");
        snapshot.SourceTimestamp.Should().Be(1_765_000_000_123);
        snapshot.Hash.Should().Be("hash");
        snapshot.Bids.Should().Equal(bid);
        snapshot.Asks.Should().Equal(ask);
        snapshot.MinimumOrderSize.Should().Be(1m);
        snapshot.TickSize.Should().Be(0.01m);
        snapshot.NegativeRisk.Should().BeTrue();
        snapshot.LastTradePrice.Should().Be(0.5m);
    }

    [Theory]
    [InlineData("-0.01", "1")]
    [InlineData("1.01", "1")]
    [InlineData("0.5", "-0.01")]
    public void Level_WithInvalidFinancialValue_ShouldRejectState(string price, string size)
    {
        var action = () => new OrderBookSnapshotLevel(
            decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(size, System.Globalization.CultureInfo.InvariantCulture));

        action.Should().Throw<ArgumentOutOfRangeException>();
    }
}
