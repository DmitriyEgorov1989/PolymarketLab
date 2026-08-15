using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;
using NormalizationModels = PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using ProjectionModels = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.OrderBooks.Models;

public sealed class OrderBookStateTests
{
    [Fact]
    public void Apply_FullSnapshot_ShouldReplaceStateAndCalculateTopOfBook()
    {
        var state = new OrderBookState("asset");
        var snapshot = CreateSnapshot(
            normalizedEventId: 42,
            sourceTimestamp: 1000,
            tickSize: 0.01m,
            bids:
            [
                Level(NormalizationModels.OrderBookSide.Bid, 0, 0.2m, 20m),
                Level(NormalizationModels.OrderBookSide.Bid, 1, 0.4m, 10m)
            ],
            asks:
            [
                Level(NormalizationModels.OrderBookSide.Ask, 0, 0.8m, 30m),
                Level(NormalizationModels.OrderBookSide.Ask, 1, 0.6m, 40m)
            ]);

        state.Apply(snapshot);

        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
        state.Bids.Keys.Should().Equal(0.2m, 0.4m);
        state.Asks.Keys.Should().Equal(0.6m, 0.8m);
        state.Bids[0.4m].Should().Be(new OrderBookLevel(0.4m, 10m));
        state.Asks[0.6m].Should().Be(new OrderBookLevel(0.6m, 40m));
        state.TickSize.Should().Be(0.01m);
        state.SourceTimestamp.Should().Be(1000);
        state.NormalizedEventId.Should().Be(42);
        state.BestBid.Should().Be(0.4m);
        state.BestAsk.Should().Be(0.6m);
        state.Spread.Should().Be(0.2m);
    }

    [Fact]
    public void Apply_SecondSnapshot_ShouldClearOldLevelsAndNullableMetadata()
    {
        var state = new OrderBookState("asset");
        state.Apply(CreateSnapshot(
            1,
            1000,
            0.01m,
            [Level(NormalizationModels.OrderBookSide.Bid, 0, 0.4m, 10m)],
            [Level(NormalizationModels.OrderBookSide.Ask, 0, 0.6m, 20m)]));

        state.Apply(CreateSnapshot(
            2,
            sourceTimestamp: null,
            tickSize: null,
            [Level(NormalizationModels.OrderBookSide.Bid, 0, 0.3m, 30m)],
            []));

        state.Bids.Keys.Should().Equal(0.3m);
        state.Asks.Should().BeEmpty();
        state.TickSize.Should().BeNull();
        state.SourceTimestamp.Should().BeNull();
        state.NormalizedEventId.Should().Be(2);
        state.BestBid.Should().Be(0.3m);
        state.BestAsk.Should().BeNull();
        state.Spread.Should().BeNull();
    }

    [Fact]
    public void Apply_EmptyFullSnapshot_ShouldBeSynchronizedWithoutTopOfBook()
    {
        var state = new OrderBookState("asset");

        state.Apply(CreateSnapshot(1, 1000, 0.01m, [], []));

        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
        state.Bids.Should().BeEmpty();
        state.Asks.Should().BeEmpty();
        state.BestBid.Should().BeNull();
        state.BestAsk.Should().BeNull();
        state.Spread.Should().BeNull();
    }

    [Fact]
    public void Apply_DifferentAsset_ShouldRejectWithoutChangingState()
    {
        var state = CreateSynchronizedState();
        var snapshot = CreateSnapshot(2, 2000, 0.001m, [], [], assetId: "other-asset");

        var action = () => state.Apply(snapshot);

        action.Should().Throw<ArgumentException>()
            .WithParameterName("book");
        AssertInitialSynchronizedState(state);
    }

    [Fact]
    public void Apply_DuplicateBidPrice_ShouldRejectWithoutChangingState()
    {
        var state = CreateSynchronizedState();
        var snapshot = CreateSnapshot(
            2,
            2000,
            0.001m,
            bids:
            [
                Level(NormalizationModels.OrderBookSide.Bid, 0, 0.3m, 1m),
                Level(NormalizationModels.OrderBookSide.Bid, 1, 0.3m, 2m)
            ],
            asks: []);

        var action = () => state.Apply(snapshot);

        action.Should().Throw<ArgumentException>()
            .WithParameterName("book");
        AssertInitialSynchronizedState(state);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Apply_NonIncreasingEventId_ShouldRejectWithoutChangingState(long eventId)
    {
        var state = CreateSynchronizedState(normalizedEventId: 2);
        var snapshot = CreateSnapshot(eventId, 2000, 0.001m, [], []);

        var action = () => state.Apply(snapshot);

        action.Should().Throw<ArgumentException>()
            .WithParameterName("book");
        AssertInitialSynchronizedState(state, normalizedEventId: 2);
    }

    [Fact]
    public void Apply_CrossedSnapshot_ShouldPreservePricesAndMarkStateSuspect()
    {
        var state = new OrderBookState("asset");
        var snapshot = CreateSnapshot(
            1,
            1000,
            0.01m,
            [Level(NormalizationModels.OrderBookSide.Bid, 0, 0.7m, 10m)],
            [Level(NormalizationModels.OrderBookSide.Ask, 0, 0.6m, 20m)]);

        state.Apply(snapshot);

        state.BestBid.Should().Be(0.7m);
        state.BestAsk.Should().Be(0.6m);
        state.Spread.Should().Be(-0.1m);
        state.Status.Should().Be(OrderBookSyncStatus.Suspect);
        state.IntegrityIssue.Should().Be(OrderBookIntegrityIssue.CrossedBook);
    }

    [Fact]
    public void Apply_ValidSnapshotAfterCrossedBook_ShouldClearIntegrityIssue()
    {
        var state = new OrderBookState("asset");
        state.Apply(CreateSnapshot(
            1,
            1000,
            0.01m,
            [Level(NormalizationModels.OrderBookSide.Bid, 0, 0.7m, 10m)],
            [Level(NormalizationModels.OrderBookSide.Ask, 0, 0.6m, 20m)]));

        state.Apply(CreateSnapshot(
            2,
            2000,
            0.01m,
            [Level(NormalizationModels.OrderBookSide.Bid, 0, 0.4m, 10m)],
            [Level(NormalizationModels.OrderBookSide.Ask, 0, 0.6m, 20m)]));

        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
        state.IntegrityIssue.Should().BeNull();
        state.Spread.Should().Be(0.2m);
    }

    private static OrderBookState CreateSynchronizedState(long normalizedEventId = 1)
    {
        var state = new OrderBookState("asset");
        state.Apply(CreateSnapshot(
            normalizedEventId,
            1000,
            0.01m,
            [Level(NormalizationModels.OrderBookSide.Bid, 0, 0.4m, 10m)],
            [Level(NormalizationModels.OrderBookSide.Ask, 0, 0.6m, 20m)]));
        return state;
    }

    private static void AssertInitialSynchronizedState(
        OrderBookState state,
        long normalizedEventId = 1)
    {
        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
        state.Bids.Should().ContainSingle()
            .Which.Value.Should().Be(new OrderBookLevel(0.4m, 10m));
        state.Asks.Should().ContainSingle()
            .Which.Value.Should().Be(new OrderBookLevel(0.6m, 20m));
        state.TickSize.Should().Be(0.01m);
        state.SourceTimestamp.Should().Be(1000);
        state.NormalizedEventId.Should().Be(normalizedEventId);
        state.BestBid.Should().Be(0.4m);
        state.BestAsk.Should().Be(0.6m);
        state.Spread.Should().Be(0.2m);
        state.IntegrityIssue.Should().BeNull();
    }

    private static ProjectionModels.BookSnapshotRecord CreateSnapshot(
        long normalizedEventId,
        long? sourceTimestamp,
        decimal? tickSize,
        IReadOnlyCollection<NormalizationModels.BookLevelRecord> bids,
        IReadOnlyCollection<NormalizationModels.BookLevelRecord> asks,
        string assetId = "asset")
    {
        return new ProjectionModels.BookSnapshotRecord(
            normalizedEventId,
            assetId,
            "condition",
            sourceTimestamp,
            "hash",
            tickSize,
            bids,
            asks);
    }

    private static NormalizationModels.BookLevelRecord Level(
        NormalizationModels.OrderBookSide side,
        int index,
        decimal price,
        decimal size)
    {
        return new NormalizationModels.BookLevelRecord(side, index, price, size);
    }
}
