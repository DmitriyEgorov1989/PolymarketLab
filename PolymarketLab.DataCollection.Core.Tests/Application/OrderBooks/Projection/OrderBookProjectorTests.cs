using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection;
using NormalizationModels = PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using ProjectionModels = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.OrderBooks.Projection;

public sealed partial class OrderBookProjectorTests
{
    private readonly IOrderBookProjector _projector = new OrderBookProjector();

    [Fact]
    public void Apply_BookSnapshot_ShouldInitializeState()
    {
        var state = new OrderBookState("asset");
        var @event = new ProjectionModels.NormalizedOrderBookEvent.BookSnapshot(
            CreateSnapshot(1));

        var result = _projector.Apply(state, @event);

        result.Outcome.Should().Be(ProjectionModels.OrderBookProjectionOutcome.Applied);
        result.IntegrityIssue.Should().BeNull();
        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
        state.BestBid.Should().Be(0.4m);
        state.BestAsk.Should().Be(0.6m);
        state.NormalizedEventId.Should().Be(1);
    }

    [Fact]
    public void Apply_EventForDifferentAsset_ShouldIgnoreWithoutChangingState()
    {
        var state = CreateSynchronizedState();
        var initialPosition = state.EventPosition;
        var @event = new ProjectionModels.NormalizedOrderBookEvent.TickSizeChange(
            new ProjectionModels.TickSizeChangeRecord(
                2,
                0,
                2,
                "other-asset",
                2000,
                0.01m,
                0.001m));

        var result = _projector.Apply(state, @event);

        result.Outcome.Should().Be(ProjectionModels.OrderBookProjectionOutcome.Ignored);
        state.EventPosition.Should().BeSameAs(initialPosition);
        state.TickSize.Should().Be(0.01m);
    }

    [Fact]
    public void Apply_MultiAssetPriceChanges_ShouldApplyOnlyStateAssetRecords()
    {
        var state = CreateSynchronizedState();
        var records = new[]
        {
            CreatePriceChange("other-asset", NormalizationModels.TradeSide.Buy, 0.3m, 50m, 0),
            CreatePriceChange("asset", NormalizationModels.TradeSide.Buy, 0.4m, 25m, 1)
        };
        var @event = new ProjectionModels.NormalizedOrderBookEvent.PriceChanges(records);

        var result = _projector.Apply(state, @event);

        result.Outcome.Should().Be(ProjectionModels.OrderBookProjectionOutcome.Applied);
        state.Bids.Should().NotContainKey(0.3m);
        state.Bids[0.4m].Size.Should().Be(25m);
        state.NormalizedEventId.Should().Be(2);
    }

    [Fact]
    public void Apply_PriceChangesWithoutStateAsset_ShouldIgnoreWithoutAdvancingPosition()
    {
        var state = CreateSynchronizedState();
        var initialPosition = state.EventPosition;
        var @event = new ProjectionModels.NormalizedOrderBookEvent.PriceChanges(
            [CreatePriceChange("other-asset", NormalizationModels.TradeSide.Buy, 0.3m, 50m, 0)]);

        var result = _projector.Apply(state, @event);

        result.Outcome.Should().Be(ProjectionModels.OrderBookProjectionOutcome.Ignored);
        state.EventPosition.Should().BeSameAs(initialPosition);
        state.Bids.Keys.Should().Equal(0.4m);
    }

    [Fact]
    public void Apply_TickSizeChange_ShouldUpdateTickSize()
    {
        var state = CreateSynchronizedState();
        var @event = new ProjectionModels.NormalizedOrderBookEvent.TickSizeChange(
            new ProjectionModels.TickSizeChangeRecord(
                2,
                0,
                2,
                "asset",
                2000,
                0.01m,
                0.001m));

        var result = _projector.Apply(state, @event);

        result.Outcome.Should().Be(ProjectionModels.OrderBookProjectionOutcome.Applied);
        result.IntegrityIssue.Should().BeNull();
        state.TickSize.Should().Be(0.001m);
    }

    [Fact]
    public void Apply_BestBidAskMismatch_ShouldReturnIntegrityIssue()
    {
        var state = CreateSynchronizedState();
        var @event = new ProjectionModels.NormalizedOrderBookEvent.BestBidAsk(
            new ProjectionModels.BestBidAskRecord(
                2,
                0,
                2,
                "asset",
                2000,
                0.3m,
                0.6m,
                0.3m));

        var result = _projector.Apply(state, @event);

        result.Outcome.Should().Be(ProjectionModels.OrderBookProjectionOutcome.Applied);
        result.IntegrityIssue.Should().NotBeNull();
        result.IntegrityIssue!.Type.Should().Be(OrderBookIntegrityIssueType.BestBidMismatch);
        state.Status.Should().Be(OrderBookSyncStatus.Suspect);
    }

    [Fact]
    public void Apply_OlderSourceTimestamp_ShouldIgnoreAndReturnIntegrityIssue()
    {
        var state = CreateSynchronizedState(sourceTimestamp: 2000);
        var initialPosition = state.EventPosition;
        var @event = new ProjectionModels.NormalizedOrderBookEvent.PriceChanges(
            [CreatePriceChange("asset", NormalizationModels.TradeSide.Buy, 0.4m, 25m, 0, 1000)]);

        var result = _projector.Apply(state, @event);

        result.Outcome.Should().Be(ProjectionModels.OrderBookProjectionOutcome.Ignored);
        result.IntegrityIssue.Should().NotBeNull();
        result.IntegrityIssue!.Type.Should().Be(OrderBookIntegrityIssueType.EventOrderViolation);
        state.EventPosition.Should().BeSameAs(initialPosition);
        state.Bids[0.4m].Size.Should().Be(10m);
    }

    [Fact]
    public void PriceChanges_ShouldOwnInputCollection()
    {
        var records = new List<ProjectionModels.PriceChangeRecord>
        {
            CreatePriceChange("asset", NormalizationModels.TradeSide.Buy, 0.4m, 25m, 0)
        };
        var @event = new ProjectionModels.NormalizedOrderBookEvent.PriceChanges(records);

        records.Clear();

        @event.Records.Should().ContainSingle();
    }

    [Fact]
    public void Apply_NullState_ShouldRejectInvalidCall()
    {
        var @event = new ProjectionModels.NormalizedOrderBookEvent.BookSnapshot(CreateSnapshot(1));

        var action = () => _projector.Apply(null!, @event);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("state");
    }

    [Fact]
    public void Apply_NullEvent_ShouldRejectInvalidCall()
    {
        var state = new OrderBookState("asset");

        var action = () => _projector.Apply(state, null!);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("event");
    }

    private static OrderBookState CreateSynchronizedState(long sourceTimestamp = 1000)
    {
        var state = new OrderBookState("asset");
        state.Apply(CreateSnapshot(1, sourceTimestamp));
        return state;
    }

    private static ProjectionModels.BookSnapshotRecord CreateSnapshot(
        long normalizedEventId,
        long sourceTimestamp = 1000,
        string assetId = "asset",
        decimal? tickSize = 0.01m,
        IReadOnlyCollection<NormalizationModels.BookLevelRecord>? bids = null,
        IReadOnlyCollection<NormalizationModels.BookLevelRecord>? asks = null)
    {
        return new ProjectionModels.BookSnapshotRecord(
            rawMessageId: normalizedEventId,
            rawItemIndex: 0,
            normalizedEventId,
            assetId,
            marketConditionId: "condition",
            sourceTimestamp,
            hash: $"hash-{normalizedEventId}",
            tickSize,
            bids ?? [Level(NormalizationModels.OrderBookSide.Bid, 0, 0.4m, 10m)],
            asks ?? [Level(NormalizationModels.OrderBookSide.Ask, 0, 0.6m, 20m)]);
    }

    private static ProjectionModels.PriceChangeRecord CreatePriceChange(
        string assetId,
        NormalizationModels.TradeSide side,
        decimal price,
        decimal size,
        int itemIndex,
        long sourceTimestamp = 2000,
        long normalizedEventId = 2)
    {
        return new ProjectionModels.PriceChangeRecord(
            rawMessageId: normalizedEventId,
            rawItemIndex: 0,
            normalizedEventId,
            assetId,
            sourceTimestamp,
            side,
            price,
            size,
            hash: null,
            bestBid: null,
            bestAsk: null,
            itemIndex);
    }

    private static NormalizationModels.BookLevelRecord Level(
        NormalizationModels.OrderBookSide side,
        int levelIndex,
        decimal price,
        decimal size)
    {
        return new NormalizationModels.BookLevelRecord(side, levelIndex, price, size);
    }
}
