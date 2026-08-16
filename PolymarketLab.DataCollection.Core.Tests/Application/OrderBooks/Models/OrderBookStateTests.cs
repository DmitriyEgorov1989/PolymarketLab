using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;
using NormalizationModels = PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using ProjectionModels = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.OrderBooks.Models;

public sealed class OrderBookStateTests
{
    private static readonly DateTimeOffset DetectedAt =
        new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

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
    public void Apply_NonIncreasingSnapshotPosition_ShouldRejectWithoutChangingState(
        long rawMessageId)
    {
        var state = CreateSynchronizedState(normalizedEventId: 2);
        var snapshot = CreateSnapshot(
            3,
            2000,
            0.001m,
            [],
            [],
            rawMessageId: rawMessageId);

        var action = () => state.Apply(snapshot);

        action.Should().Throw<ArgumentException>()
            .WithParameterName("book");
        AssertInitialSynchronizedState(state, normalizedEventId: 2);
    }

    [Fact]
    public void Apply_CrossedSnapshot_ShouldPreservePricesAndMarkStateSuspect()
    {
        var state = new OrderBookState("asset", new FixedTimeProvider(DetectedAt));
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
        state.IntegrityIssue!.Type.Should().Be(OrderBookIntegrityIssueType.CrossedBook);
        state.IntegrityIssue.Message.Should().NotBeNullOrWhiteSpace();
        state.IntegrityIssue.NormalizedEventId.Should().Be(1);
        state.IntegrityIssue.DetectedAt.Should().Be(DetectedAt);
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

    [Fact]
    public void Apply_PriceChanges_ShouldUpdateBothSidesAndTopOfBook()
    {
        var state = CreateSynchronizedState();
        var changes = new[]
        {
            Change(2, NormalizationModels.TradeSide.Sell, 0.55m, 25m, itemIndex: 1),
            Change(2, NormalizationModels.TradeSide.Buy, 0.5m, 15m, itemIndex: 0)
        };

        state.Apply(changes);

        state.Bids[0.5m].Should().Be(new OrderBookLevel(0.5m, 15m));
        state.Asks[0.55m].Should().Be(new OrderBookLevel(0.55m, 25m));
        state.BestBid.Should().Be(0.5m);
        state.BestAsk.Should().Be(0.55m);
        state.Spread.Should().Be(0.05m);
        state.SourceTimestamp.Should().Be(2000);
        state.NormalizedEventId.Should().Be(2);
        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
    }

    [Theory]
    [InlineData(NormalizationModels.TradeSide.Buy)]
    [InlineData(NormalizationModels.TradeSide.Sell)]
    public void Apply_ZeroSizePriceChange_ShouldRemoveLevel(
        NormalizationModels.TradeSide side)
    {
        var state = CreateSynchronizedState();
        var price = side == NormalizationModels.TradeSide.Buy ? 0.4m : 0.6m;

        state.Apply([Change(2, side, price, size: 0m, itemIndex: 0)]);

        var changedSide = side == NormalizationModels.TradeSide.Buy
            ? state.Bids
            : state.Asks;
        changedSide.Should().BeEmpty();
        state.Spread.Should().BeNull();
    }

    [Fact]
    public void Apply_ZeroSizeForMissingLevel_ShouldRemainSynchronized()
    {
        var state = CreateSynchronizedState();

        state.Apply([
            Change(2, NormalizationModels.TradeSide.Buy, 0.3m, size: 0m, itemIndex: 0)
        ]);

        AssertInitialSynchronizedState(state, normalizedEventId: 2, sourceTimestamp: 2000);
    }

    [Fact]
    public void Apply_PriceChanges_ShouldUseItemIndexOrder()
    {
        var state = CreateSynchronizedState();
        var changesInStorageOrder = new[]
        {
            Change(2, NormalizationModels.TradeSide.Buy, 0.4m, 20m, itemIndex: 1),
            Change(2, NormalizationModels.TradeSide.Buy, 0.4m, 15m, itemIndex: 0)
        };

        state.Apply(changesInStorageOrder);

        state.Bids[0.4m].Size.Should().Be(20m);
    }

    [Fact]
    public void Apply_InvalidThirdPriceChange_ShouldRejectWholeGroupWithoutChangingState()
    {
        var state = CreateSynchronizedState();
        var changes = new[]
        {
            Change(2, NormalizationModels.TradeSide.Buy, 0.3m, 30m, itemIndex: 0),
            Change(2, NormalizationModels.TradeSide.Sell, 0.7m, 40m, itemIndex: 1),
            Change(3, NormalizationModels.TradeSide.Buy, 0.2m, 50m, itemIndex: 2)
        };

        var action = () => state.Apply(changes);

        action.Should().Throw<ArgumentException>()
            .WithParameterName("changes");
        AssertInitialSynchronizedState(state);
    }

    [Fact]
    public void Apply_DuplicateItemIndex_ShouldRejectWholeGroupWithoutChangingState()
    {
        var state = CreateSynchronizedState();
        var changes = new[]
        {
            Change(2, NormalizationModels.TradeSide.Buy, 0.3m, 30m, itemIndex: 0),
            Change(2, NormalizationModels.TradeSide.Sell, 0.7m, 40m, itemIndex: 0)
        };

        var action = () => state.Apply(changes);

        action.Should().Throw<ArgumentException>()
            .WithParameterName("changes");
        AssertInitialSynchronizedState(state);
    }

    [Fact]
    public void Apply_CrossingAndRestoringPriceChanges_ShouldUpdateIntegrityState()
    {
        var state = CreateSynchronizedState();

        state.Apply([
            Change(2, NormalizationModels.TradeSide.Buy, 0.7m, 10m, itemIndex: 0)
        ]);

        state.Status.Should().Be(OrderBookSyncStatus.Suspect);
        state.IntegrityIssue!.Type.Should().Be(OrderBookIntegrityIssueType.CrossedBook);
        state.Spread.Should().Be(-0.1m);

        state.Apply([
            Change(3, NormalizationModels.TradeSide.Buy, 0.7m, size: 0m, itemIndex: 0),
            Change(3, NormalizationModels.TradeSide.Buy, 0.5m, 10m, itemIndex: 1)
        ]);

        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
        state.IntegrityIssue.Should().BeNull();
        state.Spread.Should().Be(0.1m);
    }

    [Fact]
    public void Apply_MatchingTickSizeChange_ShouldReplaceTickSizeWithoutChangingLevels()
    {
        var state = new OrderBookState("asset");
        state.Apply(CreateSnapshot(
            1,
            1000,
            0.01m,
            [Level(NormalizationModels.OrderBookSide.Bid, 0, 0.404m, 10m)],
            [Level(NormalizationModels.OrderBookSide.Ask, 0, 0.606m, 20m)]));

        state.Apply(TickSizeChange(2, oldTickSize: 0.0100m, newTickSize: 0.001m));

        state.TickSize.Should().Be(0.001m);
        state.Bids.Should().ContainSingle()
            .Which.Value.Should().Be(new OrderBookLevel(0.404m, 10m));
        state.Asks.Should().ContainSingle()
            .Which.Value.Should().Be(new OrderBookLevel(0.606m, 20m));
        state.BestBid.Should().Be(0.404m);
        state.BestAsk.Should().Be(0.606m);
        state.Spread.Should().Be(0.202m);
        state.SourceTimestamp.Should().Be(2000);
        state.NormalizedEventId.Should().Be(2);
        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
        state.IntegrityIssue.Should().BeNull();
    }

    [Fact]
    public void Apply_MismatchingTickSizeChange_ShouldKeepTickSizeAndMarkStateSuspect()
    {
        var state = CreateSynchronizedState(timeProvider: new FixedTimeProvider(DetectedAt));

        state.Apply(TickSizeChange(2, oldTickSize: 0.001m, newTickSize: 0.0001m));

        state.TickSize.Should().Be(0.01m);
        state.SourceTimestamp.Should().Be(2000);
        state.NormalizedEventId.Should().Be(2);
        state.Status.Should().Be(OrderBookSyncStatus.Suspect);
        state.IntegrityIssue!.Type.Should().Be(OrderBookIntegrityIssueType.TickSizeMismatch);
        state.IntegrityIssue.Message.Should().NotBeNullOrWhiteSpace();
        state.IntegrityIssue.NormalizedEventId.Should().Be(2);
        state.IntegrityIssue.DetectedAt.Should().Be(DetectedAt);
        state.Bids[0.4m].Should().Be(new OrderBookLevel(0.4m, 10m));
        state.Asks[0.6m].Should().Be(new OrderBookLevel(0.6m, 20m));
    }

    [Fact]
    public void Apply_PriceChangeAfterTickSizeMismatch_ShouldPreserveMismatch()
    {
        var state = CreateSynchronizedState();
        state.Apply(TickSizeChange(2, oldTickSize: 0.001m, newTickSize: 0.0001m));

        state.Apply([
            Change(3, NormalizationModels.TradeSide.Buy, 0.5m, 15m, itemIndex: 0)
        ]);

        state.Status.Should().Be(OrderBookSyncStatus.Suspect);
        state.IntegrityIssue!.Type.Should().Be(OrderBookIntegrityIssueType.TickSizeMismatch);
        state.BestBid.Should().Be(0.5m);
        state.NormalizedEventId.Should().Be(3);
    }

    [Fact]
    public void Apply_MatchingTickSizeChangeAfterMismatch_ShouldUpdateTickAndPreserveMismatch()
    {
        var state = CreateSynchronizedState();
        state.Apply(TickSizeChange(2, oldTickSize: 0.001m, newTickSize: 0.0001m));

        state.Apply(TickSizeChange(3, oldTickSize: 0.01m, newTickSize: 0.001m));

        state.TickSize.Should().Be(0.001m);
        state.SourceTimestamp.Should().Be(2000);
        state.NormalizedEventId.Should().Be(3);
        state.Status.Should().Be(OrderBookSyncStatus.Suspect);
        state.IntegrityIssue!.Type.Should().Be(OrderBookIntegrityIssueType.TickSizeMismatch);
    }

    [Fact]
    public void Apply_ValidSnapshotAfterTickSizeMismatch_ShouldClearIntegrityIssue()
    {
        var state = CreateSynchronizedState();
        state.Apply(TickSizeChange(2, oldTickSize: 0.001m, newTickSize: 0.0001m));

        state.Apply(CreateSnapshot(
            3,
            3000,
            0.001m,
            [Level(NormalizationModels.OrderBookSide.Bid, 0, 0.4m, 10m)],
            [Level(NormalizationModels.OrderBookSide.Ask, 0, 0.6m, 20m)]));

        state.TickSize.Should().Be(0.001m);
        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
        state.IntegrityIssue.Should().BeNull();
    }

    [Fact]
    public void Apply_TickSizeChangeBeforeSnapshot_ShouldRejectWithoutChangingState()
    {
        var state = new OrderBookState("asset");

        var action = () => state.Apply(TickSizeChange(1, 0.01m, 0.001m));

        action.Should().Throw<InvalidOperationException>();
        state.Status.Should().Be(OrderBookSyncStatus.Uninitialized);
        state.TickSize.Should().BeNull();
        state.NormalizedEventId.Should().BeNull();
    }

    [Fact]
    public void Apply_TickSizeChangeWithoutLocalTickSize_ShouldRejectWithoutChangingState()
    {
        var state = new OrderBookState("asset");
        state.Apply(CreateSnapshot(1, 1000, tickSize: null, [], []));

        var action = () => state.Apply(TickSizeChange(2, 0.01m, 0.001m));

        action.Should().Throw<InvalidOperationException>();
        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
        state.TickSize.Should().BeNull();
        state.SourceTimestamp.Should().Be(1000);
        state.NormalizedEventId.Should().Be(1);
    }

    [Fact]
    public void Apply_TickSizeChangeForDifferentAsset_ShouldRejectWithoutChangingState()
    {
        var state = CreateSynchronizedState();

        var action = () => state.Apply(TickSizeChange(
            2,
            0.01m,
            0.001m,
            assetId: "other-asset"));

        action.Should().Throw<ArgumentException>()
            .WithParameterName("change");
        AssertInitialSynchronizedState(state);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Apply_NonIncreasingTickSizeChangePosition_ShouldRejectWithoutChangingState(
        long rawMessageId)
    {
        var state = CreateSynchronizedState(normalizedEventId: 2);

        var action = () => state.Apply(TickSizeChange(
            3,
            0.01m,
            0.001m,
            rawMessageId: rawMessageId));

        action.Should().Throw<ArgumentException>()
            .WithParameterName("change");
        AssertInitialSynchronizedState(state, normalizedEventId: 2);
    }

    [Fact]
    public void Apply_MatchingBestBidAsk_ShouldUpdateCursorWithoutChangingStatus()
    {
        var state = CreateSynchronizedState();

        state.Apply(BestBidAsk(2, bestBid: 0.4m, bestAsk: 0.6m, spread: 0.2m));

        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
        state.IntegrityIssue.Should().BeNull();
        state.SourceTimestamp.Should().Be(2000);
        state.NormalizedEventId.Should().Be(2);
        state.BestBid.Should().Be(0.4m);
        state.BestAsk.Should().Be(0.6m);
        state.Spread.Should().Be(0.2m);
    }

    [Fact]
    public void Apply_MatchingBestBidAsk_ShouldPreserveExistingSuspectState()
    {
        var state = CreateSynchronizedState(timeProvider: new FixedTimeProvider(DetectedAt));
        state.Apply(TickSizeChange(2, oldTickSize: 0.001m, newTickSize: 0.0001m));
        var existingIssue = state.IntegrityIssue;

        state.Apply(BestBidAsk(3, bestBid: 0.4m, bestAsk: 0.6m, spread: 0.2m));

        state.Status.Should().Be(OrderBookSyncStatus.Suspect);
        state.IntegrityIssue.Should().BeSameAs(existingIssue);
        state.NormalizedEventId.Should().Be(3);
    }

    [Theory]
    [InlineData("0.3", "0.6", "0.2", OrderBookIntegrityIssueType.BestBidMismatch)]
    [InlineData("0.4", "0.7", "0.2", OrderBookIntegrityIssueType.BestAskMismatch)]
    [InlineData("0.4", "0.6", "0.1", OrderBookIntegrityIssueType.SpreadMismatch)]
    public void Apply_MismatchingBestBidAsk_ShouldCreateDiagnosticIssue(
        string bestBid,
        string bestAsk,
        string spread,
        OrderBookIntegrityIssueType expectedType)
    {
        var state = CreateSynchronizedState(timeProvider: new FixedTimeProvider(DetectedAt));
        var quote = BestBidAsk(
            2,
            decimal.Parse(bestBid, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(bestAsk, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(spread, System.Globalization.CultureInfo.InvariantCulture));

        state.Apply(quote);

        state.Status.Should().Be(OrderBookSyncStatus.Suspect);
        state.IntegrityIssue.Should().NotBeNull();
        state.IntegrityIssue!.Type.Should().Be(expectedType);
        state.IntegrityIssue.Message.Should().NotBeNullOrWhiteSpace();
        state.IntegrityIssue.NormalizedEventId.Should().Be(2);
        state.IntegrityIssue.DetectedAt.Should().Be(DetectedAt);
        state.SourceTimestamp.Should().Be(2000);
        state.NormalizedEventId.Should().Be(2);
        state.BestBid.Should().Be(0.4m);
        state.BestAsk.Should().Be(0.6m);
        state.Spread.Should().Be(0.2m);
    }

    [Fact]
    public void Apply_BestBidAskAgainstEmptyBook_ShouldReportBestBidMismatch()
    {
        var state = new OrderBookState("asset", new FixedTimeProvider(DetectedAt));
        state.Apply(CreateSnapshot(1, 1000, 0.01m, [], []));

        state.Apply(BestBidAsk(2, bestBid: 0m, bestAsk: 1m, spread: 1m));

        state.Status.Should().Be(OrderBookSyncStatus.Suspect);
        state.IntegrityIssue!.Type.Should().Be(OrderBookIntegrityIssueType.BestBidMismatch);
    }

    [Fact]
    public void Apply_PriceChangeAfterBestBidMismatch_ShouldPreserveIssueUntilSnapshot()
    {
        var state = CreateSynchronizedState(timeProvider: new FixedTimeProvider(DetectedAt));
        state.Apply(BestBidAsk(2, bestBid: 0.3m, bestAsk: 0.6m, spread: 0.2m));
        var existingIssue = state.IntegrityIssue;

        state.Apply([
            Change(3, NormalizationModels.TradeSide.Buy, 0.5m, 15m, itemIndex: 0)
        ]);

        state.Status.Should().Be(OrderBookSyncStatus.Suspect);
        state.IntegrityIssue.Should().BeSameAs(existingIssue);

        state.Apply(CreateSnapshot(
            4,
            4000,
            0.01m,
            [Level(NormalizationModels.OrderBookSide.Bid, 0, 0.5m, 15m)],
            [Level(NormalizationModels.OrderBookSide.Ask, 0, 0.6m, 20m)]));

        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
        state.IntegrityIssue.Should().BeNull();
    }

    [Fact]
    public void Apply_BestBidAskForDifferentAsset_ShouldRejectWithoutChangingState()
    {
        var state = CreateSynchronizedState();

        var action = () => state.Apply(BestBidAsk(
            2,
            0.4m,
            0.6m,
            0.2m,
            assetId: "other-asset"));

        action.Should().Throw<ArgumentException>()
            .WithParameterName("quote");
        AssertInitialSynchronizedState(state);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Apply_NonIncreasingBestBidAskPosition_ShouldRejectWithoutChangingState(
        long rawMessageId)
    {
        var state = CreateSynchronizedState(normalizedEventId: 2);

        var action = () => state.Apply(BestBidAsk(
            3,
            0.4m,
            0.6m,
            0.2m,
            rawMessageId: rawMessageId));

        action.Should().Throw<ArgumentException>()
            .WithParameterName("quote");
        AssertInitialSynchronizedState(state, normalizedEventId: 2);
    }

    [Fact]
    public void Apply_BestBidAskBeforeSnapshot_ShouldRejectWithoutChangingState()
    {
        var state = new OrderBookState("asset");

        var action = () => state.Apply(BestBidAsk(1, 0.4m, 0.6m, 0.2m));

        action.Should().Throw<InvalidOperationException>();
        state.Status.Should().Be(OrderBookSyncStatus.Uninitialized);
        state.IntegrityIssue.Should().BeNull();
        state.NormalizedEventId.Should().BeNull();
    }

    [Fact]
    public void Apply_EqualTimestampWithLaterArchivePosition_ShouldApplyRegardlessOfEventId()
    {
        var state = new OrderBookState("asset");
        state.Apply(CreateSnapshot(
            normalizedEventId: 100,
            sourceTimestamp: 1000,
            tickSize: 0.01m,
            [Level(NormalizationModels.OrderBookSide.Bid, 0, 0.4m, 10m)],
            [Level(NormalizationModels.OrderBookSide.Ask, 0, 0.6m, 20m)],
            rawMessageId: 10,
            rawItemIndex: 0));

        state.Apply([
            Change(
                normalizedEventId: 50,
                NormalizationModels.TradeSide.Buy,
                price: 0.5m,
                size: 15m,
                itemIndex: 0,
                sourceTimestamp: 1000,
                rawMessageId: 10,
                rawItemIndex: 1)
        ]);

        state.BestBid.Should().Be(0.5m);
        state.SourceTimestamp.Should().Be(1000);
        state.NormalizedEventId.Should().Be(50);
        state.EventPosition.Should().Be(new ProjectionModels.OrderBookEventPosition(10, 1, 50));
        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
    }

    [Theory]
    [InlineData(PastEventType.Book)]
    [InlineData(PastEventType.PriceChange)]
    [InlineData(PastEventType.TickSizeChange)]
    [InlineData(PastEventType.BestBidAsk)]
    public void Apply_LowerSourceTimestamp_ShouldRejectPayloadAndMarkEventOrderViolation(
        PastEventType eventType)
    {
        var state = CreateSynchronizedState(timeProvider: new FixedTimeProvider(DetectedAt));
        Action action = eventType switch
        {
            PastEventType.Book => () => state.Apply(CreateSnapshot(
                2,
                900,
                0.001m,
                [Level(NormalizationModels.OrderBookSide.Bid, 0, 0.5m, 30m)],
                [])),
            PastEventType.PriceChange => () => state.Apply([
                Change(
                    2,
                    NormalizationModels.TradeSide.Buy,
                    0.5m,
                    30m,
                    itemIndex: 0,
                    sourceTimestamp: 900)
            ]),
            PastEventType.TickSizeChange => () => state.Apply(
                TickSizeChange(2, 0.01m, 0.001m, sourceTimestamp: 900)),
            PastEventType.BestBidAsk => () => state.Apply(
                BestBidAsk(2, 0.5m, 0.6m, 0.1m, sourceTimestamp: 900)),
            _ => throw new ArgumentOutOfRangeException(nameof(eventType))
        };

        action();

        state.Status.Should().Be(OrderBookSyncStatus.Suspect);
        state.IntegrityIssue.Should().NotBeNull();
        state.IntegrityIssue!.Type.Should().Be(OrderBookIntegrityIssueType.EventOrderViolation);
        state.IntegrityIssue.Message.Should().NotBeNullOrWhiteSpace();
        state.IntegrityIssue.NormalizedEventId.Should().Be(2);
        state.IntegrityIssue.DetectedAt.Should().Be(DetectedAt);
        state.Bids.Should().ContainSingle()
            .Which.Value.Should().Be(new OrderBookLevel(0.4m, 10m));
        state.Asks.Should().ContainSingle()
            .Which.Value.Should().Be(new OrderBookLevel(0.6m, 20m));
        state.TickSize.Should().Be(0.01m);
        state.SourceTimestamp.Should().Be(1000);
        state.NormalizedEventId.Should().Be(1);
        state.EventPosition.Should().Be(new ProjectionModels.OrderBookEventPosition(1, 0, 1));
    }

    [Fact]
    public void Apply_NullTimestamp_ShouldNotResetTimestampWatermark()
    {
        var state = CreateSynchronizedState(timeProvider: new FixedTimeProvider(DetectedAt));
        state.Apply([
            Change(
                2,
                NormalizationModels.TradeSide.Buy,
                0.5m,
                15m,
                itemIndex: 0,
                sourceTimestamp: null)
        ]);

        state.Apply(TickSizeChange(3, 0.01m, 0.001m, sourceTimestamp: 900));

        state.BestBid.Should().Be(0.5m);
        state.TickSize.Should().Be(0.01m);
        state.SourceTimestamp.Should().BeNull();
        state.NormalizedEventId.Should().Be(2);
        state.EventPosition.Should().Be(new ProjectionModels.OrderBookEventPosition(2, 0, 2));
        state.Status.Should().Be(OrderBookSyncStatus.Suspect);
        state.IntegrityIssue!.Type.Should().Be(OrderBookIntegrityIssueType.EventOrderViolation);
        state.IntegrityIssue.NormalizedEventId.Should().Be(3);
    }

    [Fact]
    public void Apply_PriceChangesBeforeSnapshot_ShouldRejectSequenceWithoutChangingState()
    {
        var state = new OrderBookState("asset");

        var firstAction = () => state.Apply([
            Change(1, NormalizationModels.TradeSide.Buy, 0.4m, 10m, itemIndex: 0)
        ]);
        var secondAction = () => state.Apply([
            Change(2, NormalizationModels.TradeSide.Sell, 0.6m, 20m, itemIndex: 0)
        ]);

        firstAction.Should().Throw<InvalidOperationException>();
        secondAction.Should().Throw<InvalidOperationException>();
        state.Status.Should().Be(OrderBookSyncStatus.Uninitialized);
        state.Bids.Should().BeEmpty();
        state.Asks.Should().BeEmpty();
        state.TickSize.Should().BeNull();
        state.SourceTimestamp.Should().BeNull();
        state.NormalizedEventId.Should().BeNull();
        state.BestBid.Should().BeNull();
        state.BestAsk.Should().BeNull();
        state.Spread.Should().BeNull();
        state.IntegrityIssue.Should().BeNull();
    }

    private static OrderBookState CreateSynchronizedState(
        long normalizedEventId = 1,
        TimeProvider? timeProvider = null)
    {
        var state = new OrderBookState("asset", timeProvider);
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
        long normalizedEventId = 1,
        long? sourceTimestamp = 1000)
    {
        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
        state.Bids.Should().ContainSingle()
            .Which.Value.Should().Be(new OrderBookLevel(0.4m, 10m));
        state.Asks.Should().ContainSingle()
            .Which.Value.Should().Be(new OrderBookLevel(0.6m, 20m));
        state.TickSize.Should().Be(0.01m);
        state.SourceTimestamp.Should().Be(sourceTimestamp);
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
        string assetId = "asset",
        long? rawMessageId = null,
        int rawItemIndex = 0)
    {
        return new ProjectionModels.BookSnapshotRecord(
            rawMessageId ?? normalizedEventId,
            rawItemIndex,
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

    private static ProjectionModels.PriceChangeRecord Change(
        long normalizedEventId,
        NormalizationModels.TradeSide side,
        decimal price,
        decimal size,
        int itemIndex,
        string assetId = "asset",
        long? sourceTimestamp = 2000,
        long? rawMessageId = null,
        int rawItemIndex = 0)
    {
        return new ProjectionModels.PriceChangeRecord(
            rawMessageId ?? normalizedEventId,
            rawItemIndex,
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

    private static ProjectionModels.TickSizeChangeRecord TickSizeChange(
        long normalizedEventId,
        decimal oldTickSize,
        decimal newTickSize,
        string assetId = "asset",
        long? sourceTimestamp = 2000,
        long? rawMessageId = null,
        int rawItemIndex = 0)
    {
        return new ProjectionModels.TickSizeChangeRecord(
            rawMessageId ?? normalizedEventId,
            rawItemIndex,
            normalizedEventId,
            assetId,
            sourceTimestamp,
            oldTickSize,
            newTickSize);
    }

    private static ProjectionModels.BestBidAskRecord BestBidAsk(
        long normalizedEventId,
        decimal bestBid,
        decimal bestAsk,
        decimal spread,
        string assetId = "asset",
        long? sourceTimestamp = 2000,
        long? rawMessageId = null,
        int rawItemIndex = 0)
    {
        return new ProjectionModels.BestBidAskRecord(
            rawMessageId ?? normalizedEventId,
            rawItemIndex,
            normalizedEventId,
            assetId,
            sourceTimestamp,
            bestBid,
            bestAsk,
            spread);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public enum PastEventType
    {
        Book,
        PriceChange,
        TickSizeChange,
        BestBidAsk
    }
}
