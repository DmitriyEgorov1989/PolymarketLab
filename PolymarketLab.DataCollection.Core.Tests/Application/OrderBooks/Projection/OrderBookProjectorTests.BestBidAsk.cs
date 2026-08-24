using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;
using NormalizationModels = PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using ProjectionModels = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.OrderBooks.Projection;

public sealed partial class OrderBookProjectorTests
{
    [Fact]
    public void Apply_MatchingBestBidAsk_ShouldApplyWithoutIssue()
    {
        var state = CreateSynchronizedState();

        var result = _projector.Apply(state, BestBidAskEvent(0.4m, 0.6m, 0.2m));

        result.Outcome.Should().Be(ProjectionModels.OrderBookProjectionOutcome.Applied);
        result.IntegrityIssue.Should().BeNull();
        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
        state.NormalizedEventId.Should().Be(2);
    }

    [Fact]
    public void Apply_MismatchingBestAsk_ShouldReturnBestAskIssue()
    {
        var state = CreateSynchronizedState();

        var result = _projector.Apply(state, BestBidAskEvent(0.4m, 0.7m, 0.3m));

        result.IntegrityIssue!.Type.Should().Be(OrderBookIntegrityIssueType.BestAskMismatch);
        state.BestAsk.Should().Be(0.6m);
    }

    [Fact]
    public void Apply_MismatchingSpread_ShouldReturnSpreadIssue()
    {
        var state = CreateSynchronizedState();

        var result = _projector.Apply(state, BestBidAskEvent(0.4m, 0.6m, 0.1m));

        result.IntegrityIssue!.Type.Should().Be(OrderBookIntegrityIssueType.SpreadMismatch);
        state.Spread.Should().Be(0.2m);
    }

    [Fact]
    public void Apply_EmptyLocalSideAndExternalNull_ShouldMatch()
    {
        var state = new OrderBookState("asset");
        _projector.Apply(
            state,
            new ProjectionModels.NormalizedOrderBookEvent.BookSnapshot(
                CreateSnapshot(1, bids: [])));

        var result = _projector.Apply(state, BestBidAskEvent(null, 0.6m, null));

        result.Outcome.Should().Be(ProjectionModels.OrderBookProjectionOutcome.Applied);
        result.IntegrityIssue.Should().BeNull();
        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
    }

    [Fact]
    public void Apply_EmptyLocalSideAndExternalPrice_ShouldReturnMismatch()
    {
        var state = new OrderBookState("asset");
        _projector.Apply(
            state,
            new ProjectionModels.NormalizedOrderBookEvent.BookSnapshot(
                CreateSnapshot(1, bids: [])));

        var result = _projector.Apply(state, BestBidAskEvent(0.4m, 0.6m, 0.2m));

        result.Outcome.Should().Be(ProjectionModels.OrderBookProjectionOutcome.Applied);
        result.IntegrityIssue!.Type.Should().Be(OrderBookIntegrityIssueType.BestBidMismatch);
        state.BestBid.Should().BeNull();
        state.Status.Should().Be(OrderBookSyncStatus.Suspect);
    }

    private static ProjectionModels.NormalizedOrderBookEvent.BestBidAsk BestBidAskEvent(
        decimal? bestBid,
        decimal? bestAsk,
        decimal? spread)
    {
        return new ProjectionModels.NormalizedOrderBookEvent.BestBidAsk(
            new ProjectionModels.BestBidAskRecord(
                rawMessageId: 2,
                rawItemIndex: 0,
                normalizedEventId: 2,
                assetId: "asset",
                sourceTimestamp: 2000,
                bestBid,
                bestAsk,
                spread));
    }
}
