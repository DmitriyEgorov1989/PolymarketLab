using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;
using NormalizationModels = PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using ProjectionModels = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.OrderBooks.Projection;

public sealed partial class OrderBookProjectorTests
{
    [Fact]
    public void Apply_NewerBookSnapshot_ShouldReplacePreviousLevels()
    {
        var state = CreateSynchronizedState();
        var @event = new ProjectionModels.NormalizedOrderBookEvent.BookSnapshot(
            CreateSnapshot(
                2,
                bids: [Level(NormalizationModels.OrderBookSide.Bid, 0, 0.3m, 30m)],
                asks: [Level(NormalizationModels.OrderBookSide.Ask, 0, 0.7m, 40m)]));

        var result = _projector.Apply(state, @event);

        result.Outcome.Should().Be(ProjectionModels.OrderBookProjectionOutcome.Applied);
        state.Bids.Should().ContainSingle().Which.Value.Should().Be(new OrderBookLevel(0.3m, 30m));
        state.Asks.Should().ContainSingle().Which.Value.Should().Be(new OrderBookLevel(0.7m, 40m));
        state.Bids.Should().NotContainKey(0.4m);
        state.Asks.Should().NotContainKey(0.6m);
    }

    [Fact]
    public void Apply_BookSnapshotWithoutBids_ShouldKeepAskSideOnly()
    {
        var state = new OrderBookState("asset");
        var @event = new ProjectionModels.NormalizedOrderBookEvent.BookSnapshot(
            CreateSnapshot(1, bids: []));

        var result = _projector.Apply(state, @event);

        result.Outcome.Should().Be(ProjectionModels.OrderBookProjectionOutcome.Applied);
        state.Bids.Should().BeEmpty();
        state.Asks.Should().ContainSingle();
        state.BestBid.Should().BeNull();
        state.BestAsk.Should().Be(0.6m);
        state.Spread.Should().BeNull();
    }

    [Fact]
    public void Apply_BookSnapshotWithoutAsks_ShouldKeepBidSideOnly()
    {
        var state = new OrderBookState("asset");
        var @event = new ProjectionModels.NormalizedOrderBookEvent.BookSnapshot(
            CreateSnapshot(1, asks: []));

        var result = _projector.Apply(state, @event);

        result.Outcome.Should().Be(ProjectionModels.OrderBookProjectionOutcome.Applied);
        state.Bids.Should().ContainSingle();
        state.Asks.Should().BeEmpty();
        state.BestBid.Should().Be(0.4m);
        state.BestAsk.Should().BeNull();
        state.Spread.Should().BeNull();
    }

    [Fact]
    public void Apply_EmptyBookSnapshot_ShouldClearBothSides()
    {
        var state = CreateSynchronizedState();
        var @event = new ProjectionModels.NormalizedOrderBookEvent.BookSnapshot(
            CreateSnapshot(2, bids: [], asks: []));

        var result = _projector.Apply(state, @event);

        result.Outcome.Should().Be(ProjectionModels.OrderBookProjectionOutcome.Applied);
        state.Bids.Should().BeEmpty();
        state.Asks.Should().BeEmpty();
        state.BestBid.Should().BeNull();
        state.BestAsk.Should().BeNull();
        state.Spread.Should().BeNull();
    }

    [Fact]
    public void Apply_BookSnapshot_ShouldCalculateHighestBid()
    {
        var state = new OrderBookState("asset");
        var @event = new ProjectionModels.NormalizedOrderBookEvent.BookSnapshot(
            CreateSnapshot(
                1,
                bids:
                [
                    Level(NormalizationModels.OrderBookSide.Bid, 0, 0.2m, 20m),
                    Level(NormalizationModels.OrderBookSide.Bid, 1, 0.5m, 10m),
                    Level(NormalizationModels.OrderBookSide.Bid, 2, 0.4m, 30m)
                ]));

        _projector.Apply(state, @event);

        state.BestBid.Should().Be(0.5m);
    }

    [Fact]
    public void Apply_BookSnapshot_ShouldCalculateLowestAsk()
    {
        var state = new OrderBookState("asset");
        var @event = new ProjectionModels.NormalizedOrderBookEvent.BookSnapshot(
            CreateSnapshot(
                1,
                asks:
                [
                    Level(NormalizationModels.OrderBookSide.Ask, 0, 0.8m, 20m),
                    Level(NormalizationModels.OrderBookSide.Ask, 1, 0.55m, 10m),
                    Level(NormalizationModels.OrderBookSide.Ask, 2, 0.6m, 30m)
                ]));

        _projector.Apply(state, @event);

        state.BestAsk.Should().Be(0.55m);
    }

    [Fact]
    public void Apply_BookSnapshot_ShouldCalculateSpreadFromBestPrices()
    {
        var state = new OrderBookState("asset");
        var @event = new ProjectionModels.NormalizedOrderBookEvent.BookSnapshot(
            CreateSnapshot(
                1,
                bids: [Level(NormalizationModels.OrderBookSide.Bid, 0, 0.45m, 10m)],
                asks: [Level(NormalizationModels.OrderBookSide.Ask, 0, 0.55m, 20m)]));

        _projector.Apply(state, @event);

        state.Spread.Should().Be(0.10m);
    }

    [Fact]
    public void Apply_BookSnapshotForDifferentAsset_ShouldIgnoreWithoutChangingState()
    {
        var state = CreateSynchronizedState();
        var initialPosition = state.EventPosition;
        var @event = new ProjectionModels.NormalizedOrderBookEvent.BookSnapshot(
            CreateSnapshot(2, assetId: "other-asset"));

        var result = _projector.Apply(state, @event);

        result.Outcome.Should().Be(ProjectionModels.OrderBookProjectionOutcome.Ignored);
        state.EventPosition.Should().BeSameAs(initialPosition);
        state.Bids.Keys.Should().Equal(0.4m);
        state.Asks.Keys.Should().Equal(0.6m);
    }

    [Fact]
    public void Apply_CrossedBookSnapshot_ShouldPreserveLevelsAndReturnIntegrityIssue()
    {
        var state = new OrderBookState("asset");
        var @event = new ProjectionModels.NormalizedOrderBookEvent.BookSnapshot(
            CreateSnapshot(
                1,
                bids: [Level(NormalizationModels.OrderBookSide.Bid, 0, 0.7m, 10m)],
                asks: [Level(NormalizationModels.OrderBookSide.Ask, 0, 0.6m, 20m)]));

        var result = _projector.Apply(state, @event);

        result.Outcome.Should().Be(ProjectionModels.OrderBookProjectionOutcome.Applied);
        result.IntegrityIssue!.Type.Should().Be(OrderBookIntegrityIssueType.CrossedBook);
        state.BestBid.Should().Be(0.7m);
        state.BestAsk.Should().Be(0.6m);
        state.Spread.Should().Be(-0.1m);
        state.Status.Should().Be(OrderBookSyncStatus.Suspect);
    }
}
