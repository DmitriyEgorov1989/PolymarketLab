using FluentAssertions;
using NormalizationModels = PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using ProjectionModels = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.OrderBooks.Projection.Models;

public sealed class ProjectorInputModelsTests
{
    [Fact]
    public void BookSnapshotRecord_ShouldPreserveNormalizedProjectionAndOwnLevelCollections()
    {
        var bid = new NormalizationModels.BookLevelRecord(
            NormalizationModels.OrderBookSide.Bid,
            0,
            0.4m,
            10m);
        var ask = new NormalizationModels.BookLevelRecord(
            NormalizationModels.OrderBookSide.Ask,
            0,
            0.6m,
            20m);
        var bids = new List<NormalizationModels.BookLevelRecord> { bid };
        var asks = new List<NormalizationModels.BookLevelRecord> { ask };

        var record = new ProjectionModels.BookSnapshotRecord(
            normalizedEventId: 42,
            assetId: "asset",
            marketConditionId: "condition",
            sourceTimestamp: null,
            hash: "hash",
            tickSize: null,
            bids,
            asks);
        bids.Clear();
        asks.Clear();

        record.NormalizedEventId.Should().Be(42);
        record.AssetId.Should().Be("asset");
        record.MarketConditionId.Should().Be("condition");
        record.SourceTimestamp.Should().BeNull();
        record.Hash.Should().Be("hash");
        record.TickSize.Should().BeNull();
        record.Bids.Should().Equal(bid);
        record.Asks.Should().Equal(ask);
    }

    [Fact]
    public void BookSnapshotRecord_LevelOnWrongSide_ShouldRejectInvalidState()
    {
        var ask = new NormalizationModels.BookLevelRecord(
            NormalizationModels.OrderBookSide.Ask,
            0,
            0.6m,
            1m);

        var action = () => new ProjectionModels.BookSnapshotRecord(
            1,
            "asset",
            "condition",
            1000,
            "hash",
            0.01m,
            [ask],
            []);

        action.Should().Throw<ArgumentException>()
            .WithParameterName("bids");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.01")]
    public void BookSnapshotRecord_NonPositiveTickSize_ShouldRejectInvalidState(string tickSize)
    {
        var action = () => new ProjectionModels.BookSnapshotRecord(
            1,
            "asset",
            "condition",
            1000,
            "hash",
            decimal.Parse(tickSize, System.Globalization.CultureInfo.InvariantCulture),
            [],
            []);

        action.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("tickSize");
    }

    [Fact]
    public void PriceChangeRecord_ShouldPreserveNormalizedProjection()
    {
        var record = new ProjectionModels.PriceChangeRecord(
            normalizedEventId: 43,
            assetId: "asset",
            sourceTimestamp: 1001,
            side: NormalizationModels.TradeSide.Buy,
            price: 0.4m,
            size: 0m,
            hash: null,
            bestBid: null,
            bestAsk: 0.6m,
            itemIndex: 2);

        record.NormalizedEventId.Should().Be(43);
        record.AssetId.Should().Be("asset");
        record.SourceTimestamp.Should().Be(1001);
        record.Side.Should().Be(NormalizationModels.TradeSide.Buy);
        record.Price.Should().Be(0.4m);
        record.Size.Should().Be(0m);
        record.Hash.Should().BeNull();
        record.BestBid.Should().BeNull();
        record.BestAsk.Should().Be(0.6m);
        record.ItemIndex.Should().Be(2);
    }

    [Fact]
    public void TickSizeChangeRecord_ShouldPreserveNormalizedProjection()
    {
        var record = new ProjectionModels.TickSizeChangeRecord(
            normalizedEventId: 44,
            assetId: "asset",
            sourceTimestamp: 1002,
            oldTickSize: 0m,
            newTickSize: 0.001m);

        record.NormalizedEventId.Should().Be(44);
        record.AssetId.Should().Be("asset");
        record.SourceTimestamp.Should().Be(1002);
        record.OldTickSize.Should().Be(0m);
        record.NewTickSize.Should().Be(0.001m);
    }

    [Fact]
    public void BestBidAskRecord_ShouldPreserveNormalizedProjection()
    {
        var record = new ProjectionModels.BestBidAskRecord(
            normalizedEventId: 45,
            assetId: "asset",
            sourceTimestamp: null,
            bestBid: 0.4m,
            bestAsk: 0.6m,
            spread: 0.2m);

        record.NormalizedEventId.Should().Be(45);
        record.AssetId.Should().Be("asset");
        record.SourceTimestamp.Should().BeNull();
        record.BestBid.Should().Be(0.4m);
        record.BestAsk.Should().Be(0.6m);
        record.Spread.Should().Be(0.2m);
    }

    [Fact]
    public void ProjectorInputRecord_NonPositiveNormalizedEventId_ShouldRejectInvalidState()
    {
        var action = () => new ProjectionModels.TickSizeChangeRecord(
            0,
            "asset",
            null,
            0.01m,
            0.001m);

        action.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("normalizedEventId");
    }

    [Fact]
    public void PriceChangeRecord_InvalidItemIndex_ShouldRejectInvalidState()
    {
        var action = () => new ProjectionModels.PriceChangeRecord(
            1,
            "asset",
            null,
            NormalizationModels.TradeSide.Sell,
            0.5m,
            1m,
            null,
            null,
            null,
            -1);

        action.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("itemIndex");
    }
}
