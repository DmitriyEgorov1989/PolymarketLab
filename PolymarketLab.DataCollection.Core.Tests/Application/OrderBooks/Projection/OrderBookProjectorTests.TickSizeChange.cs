using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;
using NormalizationModels = PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using ProjectionModels = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.OrderBooks.Projection;

public sealed partial class OrderBookProjectorTests
{
    [Fact]
    public void Apply_MatchingOldTickSize_ShouldApplyNewTickSizeWithoutIssue()
    {
        var state = CreateSynchronizedState();
        var @event = TickSizeChangeEvent(oldTickSize: 0.0100m, newTickSize: 0.001m);

        var result = _projector.Apply(state, @event);

        result.Outcome.Should().Be(ProjectionModels.OrderBookProjectionOutcome.Applied);
        result.IntegrityIssue.Should().BeNull();
        state.TickSize.Should().Be(0.001m);
        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
    }

    [Fact]
    public void Apply_MismatchingOldTickSize_ShouldKeepCurrentTickSizeAndReturnIssue()
    {
        var state = CreateSynchronizedState();
        var @event = TickSizeChangeEvent(oldTickSize: 0.02m, newTickSize: 0.001m);

        var result = _projector.Apply(state, @event);

        result.Outcome.Should().Be(ProjectionModels.OrderBookProjectionOutcome.Applied);
        result.IntegrityIssue!.Type.Should().Be(OrderBookIntegrityIssueType.TickSizeMismatch);
        state.TickSize.Should().Be(0.01m);
        state.Status.Should().Be(OrderBookSyncStatus.Suspect);
    }

    [Fact]
    public void Apply_TickSizeChange_ShouldNotRoundExistingLevels()
    {
        var state = new OrderBookState("asset");
        _projector.Apply(
            state,
            new ProjectionModels.NormalizedOrderBookEvent.BookSnapshot(
                CreateSnapshot(
                    1,
                    tickSize: 0.01m,
                    bids: [Level(NormalizationModels.OrderBookSide.Bid, 0, 0.404m, 10m)],
                    asks: [Level(NormalizationModels.OrderBookSide.Ask, 0, 0.606m, 20m)])));

        var result = _projector.Apply(
            state,
            TickSizeChangeEvent(oldTickSize: 0.01m, newTickSize: 0.001m));

        result.Outcome.Should().Be(ProjectionModels.OrderBookProjectionOutcome.Applied);
        state.TickSize.Should().Be(0.001m);
        state.Bids.Should().ContainKey(0.404m).WhoseValue.Size.Should().Be(10m);
        state.Asks.Should().ContainKey(0.606m).WhoseValue.Size.Should().Be(20m);
        state.BestBid.Should().Be(0.404m);
        state.BestAsk.Should().Be(0.606m);
    }

    private static ProjectionModels.NormalizedOrderBookEvent.TickSizeChange TickSizeChangeEvent(
        decimal oldTickSize,
        decimal newTickSize)
    {
        return new ProjectionModels.NormalizedOrderBookEvent.TickSizeChange(
            new ProjectionModels.TickSizeChangeRecord(
                rawMessageId: 2,
                rawItemIndex: 0,
                normalizedEventId: 2,
                assetId: "asset",
                sourceTimestamp: 2000,
                oldTickSize,
                newTickSize));
    }
}
