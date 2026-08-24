using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;
using NormalizationModels = PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using ProjectionModels = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.OrderBooks.Projection;

public sealed partial class OrderBookProjectorTests
{
    [Fact]
    public void Apply_PriceChange_ShouldAddNewBid()
    {
        var state = CreateSynchronizedState();

        ApplyPriceChanges(state, CreatePriceChange("asset", NormalizationModels.TradeSide.Buy, 0.3m, 30m, 0));

        state.Bids[0.3m].Should().Be(new OrderBookLevel(0.3m, 30m));
    }

    [Fact]
    public void Apply_PriceChange_ShouldUpdateExistingBid()
    {
        var state = CreateSynchronizedState();

        ApplyPriceChanges(state, CreatePriceChange("asset", NormalizationModels.TradeSide.Buy, 0.4m, 25m, 0));

        state.Bids[0.4m].Should().Be(new OrderBookLevel(0.4m, 25m));
    }

    [Fact]
    public void Apply_PriceChangeWithZeroSize_ShouldDeleteBid()
    {
        var state = CreateSynchronizedState();

        ApplyPriceChanges(state, CreatePriceChange("asset", NormalizationModels.TradeSide.Buy, 0.4m, 0m, 0));

        state.Bids.Should().BeEmpty();
        state.BestBid.Should().BeNull();
    }

    [Fact]
    public void Apply_PriceChange_ShouldAddNewAsk()
    {
        var state = CreateSynchronizedState();

        ApplyPriceChanges(state, CreatePriceChange("asset", NormalizationModels.TradeSide.Sell, 0.7m, 30m, 0));

        state.Asks[0.7m].Should().Be(new OrderBookLevel(0.7m, 30m));
    }

    [Fact]
    public void Apply_PriceChange_ShouldUpdateExistingAsk()
    {
        var state = CreateSynchronizedState();

        ApplyPriceChanges(state, CreatePriceChange("asset", NormalizationModels.TradeSide.Sell, 0.6m, 25m, 0));

        state.Asks[0.6m].Should().Be(new OrderBookLevel(0.6m, 25m));
    }

    [Fact]
    public void Apply_PriceChangeWithZeroSize_ShouldDeleteAsk()
    {
        var state = CreateSynchronizedState();

        ApplyPriceChanges(state, CreatePriceChange("asset", NormalizationModels.TradeSide.Sell, 0.6m, 0m, 0));

        state.Asks.Should().BeEmpty();
        state.BestAsk.Should().BeNull();
    }

    [Fact]
    public void Apply_PriceChange_ShouldChangeBestBid()
    {
        var state = CreateSynchronizedState();

        ApplyPriceChanges(state, CreatePriceChange("asset", NormalizationModels.TradeSide.Buy, 0.5m, 15m, 0));

        state.BestBid.Should().Be(0.5m);
        state.Spread.Should().Be(0.1m);
    }

    [Fact]
    public void Apply_PriceChange_ShouldChangeBestAsk()
    {
        var state = CreateSynchronizedState();

        ApplyPriceChanges(state, CreatePriceChange("asset", NormalizationModels.TradeSide.Sell, 0.5m, 15m, 0));

        state.BestAsk.Should().Be(0.5m);
        state.Spread.Should().Be(0.1m);
    }

    [Fact]
    public void Apply_NonBestPriceChange_ShouldNotChangeTopOfBook()
    {
        var state = CreateSynchronizedState();

        ApplyPriceChanges(state, CreatePriceChange("asset", NormalizationModels.TradeSide.Buy, 0.3m, 15m, 0));

        state.BestBid.Should().Be(0.4m);
        state.BestAsk.Should().Be(0.6m);
        state.Spread.Should().Be(0.2m);
    }

    [Fact]
    public void Apply_PriceChanges_ShouldApplyMultipleChangesAtomically()
    {
        var state = CreateSynchronizedState();
        var @event = new ProjectionModels.NormalizedOrderBookEvent.PriceChanges(
        [
            CreatePriceChange("asset", NormalizationModels.TradeSide.Buy, 0.4m, 0m, 0),
            CreatePriceChange("asset", NormalizationModels.TradeSide.Buy, 0.5m, 15m, 1),
            CreatePriceChange("asset", NormalizationModels.TradeSide.Sell, 0.55m, 25m, 2)
        ]);

        var result = _projector.Apply(state, @event);

        result.Outcome.Should().Be(ProjectionModels.OrderBookProjectionOutcome.Applied);
        state.Bids.Keys.Should().Equal(0.5m);
        state.Asks.Keys.Should().Equal(0.55m, 0.6m);
        state.Spread.Should().Be(0.05m);
    }

    [Fact]
    public void Apply_PriceChanges_ShouldRespectItemIndexOrder()
    {
        var state = CreateSynchronizedState();
        var @event = new ProjectionModels.NormalizedOrderBookEvent.PriceChanges(
        [
            CreatePriceChange("asset", NormalizationModels.TradeSide.Buy, 0.4m, 20m, 1),
            CreatePriceChange("asset", NormalizationModels.TradeSide.Buy, 0.4m, 15m, 0)
        ]);

        _projector.Apply(state, @event);

        state.Bids[0.4m].Size.Should().Be(20m);
    }

    private void ApplyPriceChanges(
        OrderBookState state,
        params ProjectionModels.PriceChangeRecord[] records)
    {
        var result = _projector.Apply(
            state,
            new ProjectionModels.NormalizedOrderBookEvent.PriceChanges(records));

        result.Outcome.Should().Be(ProjectionModels.OrderBookProjectionOutcome.Applied);
    }
}
