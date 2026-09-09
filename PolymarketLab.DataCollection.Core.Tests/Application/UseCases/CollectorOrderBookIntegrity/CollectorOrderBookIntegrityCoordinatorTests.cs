using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorOrderBookIntegrity;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;
using ProjectionBestBidAskRecord = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models.BestBidAskRecord;
using ProjectionBookSnapshotRecord = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models.BookSnapshotRecord;

namespace PolymarketLab.DataCollection.Core.Tests.Application.UseCases.CollectorOrderBookIntegrity;

public sealed class CollectorOrderBookIntegrityCoordinatorTests
{
    [Fact]
    public async Task EvaluateAsync_WithConsistentCommittedEvents_ShouldSucceed()
    {
        var reader = new Reader(
        [
            Snapshot(1, 0, 11, bestBid: 0.40m, bestAsk: 0.60m),
            new NormalizedOrderBookEvent.BestBidAsk(new ProjectionBestBidAskRecord(
                2, 0, 12, "asset", 101, 0.40m, 0.60m, 0.20m))
        ]);
        var coordinator = new CollectorOrderBookIntegrityCoordinator(
            reader,
            new OrderBookProjector(),
            NullLogger<CollectorOrderBookIntegrityCoordinator>.Instance);

        var result = await coordinator.EvaluateAsync(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            3,
            [TokenId.Create("asset").Value],
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        reader.ProjectionVersions.Should().Equal(3);
    }

    [Fact]
    public async Task EvaluateAsync_WithFirstIntegrityIssue_ShouldReturnSafeFailure()
    {
        var reader = new Reader(
        [
            Snapshot(1, 0, 11, bestBid: 0.40m, bestAsk: 0.60m),
            new NormalizedOrderBookEvent.BestBidAsk(new ProjectionBestBidAskRecord(
                2, 0, 12, "asset", 101, 0.30m, 0.60m, 0.30m)),
            Snapshot(3, 0, 13, bestBid: 0.45m, bestAsk: 0.55m)
        ]);
        var projector = new CountingProjector(new OrderBookProjector());
        var coordinator = new CollectorOrderBookIntegrityCoordinator(
            reader,
            projector,
            NullLogger<CollectorOrderBookIntegrityCoordinator>.Instance);

        var result = await coordinator.EvaluateAsync(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            3,
            [TokenId.Create("asset").Value],
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.order_book.integrity.issue");
        result.Error.Message.Should().NotContain("Local best bid");
        projector.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task EvaluateAsync_WhenReaderThrows_ShouldReturnSafeFailure()
    {
        var reader = new Reader([])
        {
            Exception = new InvalidOperationException("mapped database value")
        };
        var coordinator = new CollectorOrderBookIntegrityCoordinator(
            reader,
            new OrderBookProjector(),
            NullLogger<CollectorOrderBookIntegrityCoordinator>.Instance);

        var result = await coordinator.EvaluateAsync(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            3,
            [TokenId.Create("asset").Value],
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.order_book.integrity.read_failed");
        result.Error.Message.Should().NotContain("mapped database value");
    }

    [Fact]
    public async Task EvaluateAsync_WhenExpectedAssetHasNoSnapshot_ShouldReturnIntegrityFailure()
    {
        var coordinator = new CollectorOrderBookIntegrityCoordinator(
            new Reader([Snapshot(1, 0, 11, bestBid: 0.40m, bestAsk: 0.60m)]),
            new OrderBookProjector(),
            NullLogger<CollectorOrderBookIntegrityCoordinator>.Instance);

        var result = await coordinator.EvaluateAsync(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            3,
            [TokenId.Create("asset").Value, TokenId.Create("missing-asset").Value],
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.order_book.integrity.issue");
        result.Error.Message.Should().Contain("missing-asset");
    }

    private static NormalizedOrderBookEvent Snapshot(
        long rawMessageId,
        int rawItemIndex,
        long eventId,
        decimal bestBid,
        decimal bestAsk) =>
        new NormalizedOrderBookEvent.BookSnapshot(new ProjectionBookSnapshotRecord(
            rawMessageId,
            rawItemIndex,
            eventId,
            "asset",
            "condition",
            100,
            "hash",
            0.01m,
            [new BookLevelRecord(OrderBookSide.Bid, 0, bestBid, 10)],
            [new BookLevelRecord(OrderBookSide.Ask, 0, bestAsk, 10)]));

    private sealed class Reader(IReadOnlyList<NormalizedOrderBookEvent> events)
        : IOrderBookIntegrityReader
    {
        public List<int> ProjectionVersions { get; } = [];
        public Exception? Exception { get; init; }

        public Task<IReadOnlyList<NormalizedOrderBookEvent>> ReadAsync(
            CollectorSessionId sessionId,
            int projectionVersion,
            CancellationToken cancellationToken)
        {
            ProjectionVersions.Add(projectionVersion);
            if (Exception is not null)
                throw Exception;
            return Task.FromResult(events);
        }
    }

    private sealed class CountingProjector(IOrderBookProjector inner) : IOrderBookProjector
    {
        public int CallCount { get; private set; }

        public OrderBookProjectionResult Apply(
            PolymarketLab.DataCollection.Core.Application.OrderBooks.Models.OrderBookState state,
            NormalizedOrderBookEvent @event)
        {
            CallCount++;
            return inner.Apply(state, @event);
        }
    }
}
