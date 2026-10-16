using FluentAssertions;
using Microsoft.Extensions.Logging;
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
using ProjectionPriceChangeRecord = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models.PriceChangeRecord;
using ProjectionTickSizeChangeRecord = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models.TickSizeChangeRecord;
using OrderBookIntegrityIssueType = PolymarketLab.DataCollection.Core.Application.OrderBooks.Models.OrderBookIntegrityIssueType;

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
            null,
            null,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        reader.ProjectionVersions.Should().Equal(3);
    }

    [Fact]
    public async Task EvaluateAsync_WithRepeatedPreBatchBestAtSameTimestamp_ShouldSucceed()
    {
        const long timestamp = 1792055389015;
        var coordinator = new CollectorOrderBookIntegrityCoordinator(
            new Reader(
            [
                SnapshotWithLevels(
                    100,
                    200,
                    timestamp - 1,
                    1,
                    [new BookLevelRecord(OrderBookSide.Bid, 0, 0.68m, 10m)],
                    [
                        new BookLevelRecord(OrderBookSide.Ask, 0, 0.69m, 10m),
                        new BookLevelRecord(OrderBookSide.Ask, 1, 0.70m, 10m)
                    ]),
                PriceChanges(new ProjectionPriceChangeRecord(
                    101, 0, 201, "asset", timestamp, TradeSide.Sell, 0.71m, 960.33m,
                    "hash", 0.68m, 0.69m, 0, connectionEpoch: 1)),
                PriceChanges(new ProjectionPriceChangeRecord(
                    102, 0, 202, "asset", timestamp, TradeSide.Sell, 0.69m, 0m,
                    "hash", 0.68m, 0.69m, 0, connectionEpoch: 1)),
                PriceChanges(new ProjectionPriceChangeRecord(
                    103, 0, 203, "asset", timestamp, TradeSide.Sell, 0.70m, 0m,
                    "hash", 0.68m, 0.69m, 0, connectionEpoch: 1)),
                SnapshotWithLevels(
                    104,
                    204,
                    timestamp,
                    1,
                    [new BookLevelRecord(OrderBookSide.Bid, 0, 0.68m, 10m)],
                    [new BookLevelRecord(OrderBookSide.Ask, 0, 0.71m, 960.33m)])
            ]),
            new OrderBookProjector(),
            NullLogger<CollectorOrderBookIntegrityCoordinator>.Instance);

        var result = await coordinator.EvaluateAsync(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            1,
            [TokenId.Create("asset").Value],
            null,
            null,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_WithFirstIntegrityIssue_ShouldReturnSafeFailure()
    {
        var reader = new Reader(
        [
            Snapshot(1, 0, 11, bestBid: 0.40m, bestAsk: 0.60m),
            PriceChanges(new ProjectionPriceChangeRecord(
                2, 0, 12, "asset", 101, TradeSide.Buy, 0.30m, 10m,
                "hash", 0.50m, 0.60m, 0)),
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
            null,
            null,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.order_book.integrity.issue");
        result.Error.Message.Should().NotContain("Local best bid");
        projector.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task EvaluateAsync_WithUnknownTickSize_ShouldReturnIntegrityFailureAndLogProvenance()
    {
        var logger = new TestLogger();
        var coordinator = new CollectorOrderBookIntegrityCoordinator(
            new Reader(
            [
                new NormalizedOrderBookEvent.BookSnapshot(new ProjectionBookSnapshotRecord(
                    1, 0, 11, "asset", "condition", 100, "hash", null, [], [], 1)),
                new NormalizedOrderBookEvent.TickSizeChange(new ProjectionTickSizeChangeRecord(
                    2, 0, 12, "asset", 101, 0.01m, 0.001m, 1))
            ]),
            new OrderBookProjector(),
            logger);

        var result = await coordinator.EvaluateAsync(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            3,
            [TokenId.Create("asset").Value],
            null,
            null,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.order_book.integrity.issue");
        logger.Entries.Should().ContainSingle();
        logger.Entries.Single().Should().Contain(new Dictionary<string, object?>
        {
            ["IssueType"] = OrderBookIntegrityIssueType.TickSizeUnknown,
            ["LocalTickSize"] = null,
            ["OldTickSize"] = 0.01m,
            ["NewTickSize"] = 0.001m
        });
    }

    [Fact]
    public async Task EvaluateAsync_WithStandaloneQuoteAheadOfLevelDeltas_ShouldSucceed()
    {
        var coordinator = new CollectorOrderBookIntegrityCoordinator(
            new Reader(
            [
                Snapshot(1, 0, 11, bestBid: 0.49m, bestAsk: 0.50m),
                PriceChanges(new ProjectionPriceChangeRecord(
                    2, 0, 12, "asset", 1791043143069, TradeSide.Buy, 0.49m, 10m,
                    "hash", 0.49m, 0.50m, 0)),
                new NormalizedOrderBookEvent.BestBidAsk(new ProjectionBestBidAskRecord(
                    3, 0, 13, "asset", 1791043143091, 0.50m, 0.51m, 0.01m))
            ]),
            new OrderBookProjector(),
            NullLogger<CollectorOrderBookIntegrityCoordinator>.Instance);

        var result = await coordinator.EvaluateAsync(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            3,
            [TokenId.Create("asset").Value],
            null,
            null,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_WithSourceTimestampInversionWithinEpoch_ShouldReplayInSourceOrder()
    {
        var coordinator = new CollectorOrderBookIntegrityCoordinator(
            new Reader(
            [
                Snapshot(
                    1,
                    0,
                    11,
                    bestBid: 0.40m,
                    bestAsk: 0.60m,
                    sourceTimestamp: 100,
                    connectionEpoch: 1),
                PriceChanges(new ProjectionPriceChangeRecord(
                    2, 0, 12, "asset", 102, TradeSide.Sell, 0.61m, 10m,
                    "hash-2", 0.50m, 0.60m, 0, connectionEpoch: 1)),
                PriceChanges(new ProjectionPriceChangeRecord(
                    3, 0, 13, "asset", 101, TradeSide.Buy, 0.50m, 10m,
                    "hash-1", 0.50m, 0.60m, 0, connectionEpoch: 1))
            ]),
            new OrderBookProjector(),
            NullLogger<CollectorOrderBookIntegrityCoordinator>.Instance);

        var result = await coordinator.EvaluateAsync(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            3,
            [TokenId.Create("asset").Value],
            null,
            null,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_WithMultiAssetSourceTimestampInversion_ShouldReplayEachAssetIndependently()
    {
        var coordinator = new CollectorOrderBookIntegrityCoordinator(
            new Reader(
            [
                Snapshot(
                    1, 0, 11, 0.40m, 0.60m, 100, 1,
                    assetId: "asset-a"),
                Snapshot(
                    2, 0, 12, 0.30m, 0.70m, 100, 1,
                    assetId: "asset-b"),
                PriceChanges(
                    new ProjectionPriceChangeRecord(
                        3, 0, 13, "asset-a", 102, TradeSide.Sell, 0.61m, 10m,
                        "hash-a-2", 0.50m, 0.60m, 0, connectionEpoch: 1),
                    new ProjectionPriceChangeRecord(
                        3, 0, 13, "asset-b", 102, TradeSide.Sell, 0.71m, 10m,
                        "hash-b-2", 0.40m, 0.70m, 1, connectionEpoch: 1)),
                PriceChanges(
                    new ProjectionPriceChangeRecord(
                        4, 0, 14, "asset-a", 101, TradeSide.Buy, 0.45m, 5m,
                        "hash-a-1", 0.45m, 0.60m, 0, connectionEpoch: 1),
                    new ProjectionPriceChangeRecord(
                        4, 0, 14, "asset-b", 101, TradeSide.Buy, 0.40m, 10m,
                        "hash-b-1", 0.40m, 0.70m, 1, connectionEpoch: 1),
                    new ProjectionPriceChangeRecord(
                        4, 0, 14, "asset-a", 101, TradeSide.Buy, 0.50m, 10m,
                        "hash-a-1", 0.50m, 0.60m, 2, connectionEpoch: 1))
            ]),
            new OrderBookProjector(),
            NullLogger<CollectorOrderBookIntegrityCoordinator>.Instance);

        var result = await coordinator.EvaluateAsync(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            3,
            [TokenId.Create("asset-a").Value, TokenId.Create("asset-b").Value],
            null,
            null,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_WithNullTimestampBarrier_ShouldNotReorderAcrossBarrier()
    {
        var coordinator = new CollectorOrderBookIntegrityCoordinator(
            new Reader(
            [
                Snapshot(
                    1, 0, 11, 0.40m, 0.60m, 100, 1),
                new NormalizedOrderBookEvent.BestBidAsk(new ProjectionBestBidAskRecord(
                    2, 0, 12, "asset", 102, 0.40m, 0.60m, 0.20m, 1)),
                new NormalizedOrderBookEvent.BestBidAsk(new ProjectionBestBidAskRecord(
                    3, 0, 13, "asset", null, 0.40m, 0.60m, 0.20m, 1)),
                new NormalizedOrderBookEvent.BestBidAsk(new ProjectionBestBidAskRecord(
                    4, 0, 14, "asset", 101, 0.40m, 0.60m, 0.20m, 1))
            ]),
            new OrderBookProjector(),
            NullLogger<CollectorOrderBookIntegrityCoordinator>.Instance);

        var result = await coordinator.EvaluateAsync(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            3,
            [TokenId.Create("asset").Value],
            null,
            null,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.order_book.integrity.issue");
    }

    [Fact]
    public async Task EvaluateAsync_WithNewEpochDeltaBeforeSnapshot_ShouldReturnMissingSnapshot()
    {
        var coordinator = new CollectorOrderBookIntegrityCoordinator(
            new Reader(
            [
                Snapshot(1, 0, 11, 0.40m, 0.60m, 100, 1),
                PriceChanges(new ProjectionPriceChangeRecord(
                    2, 0, 12, "asset", 101, TradeSide.Buy, 0.50m, 10m,
                    "hash", 0.50m, 0.60m, 0, connectionEpoch: 2)),
                Snapshot(3, 0, 13, 0.50m, 0.60m, 102, 2)
            ]),
            new OrderBookProjector(),
            NullLogger<CollectorOrderBookIntegrityCoordinator>.Instance);

        var result = await coordinator.EvaluateAsync(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            3,
            [TokenId.Create("asset").Value],
            null,
            null,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("initial snapshot");
    }

    [Fact]
    public async Task EvaluateAsync_WhenAssetHasNoSnapshotForKnownEpoch_ShouldReturnMissingSnapshot()
    {
        var coordinator = new CollectorOrderBookIntegrityCoordinator(
            new Reader(
            [
                Snapshot(1, 0, 11, 0.40m, 0.60m, 100, 1, assetId: "asset-a"),
                Snapshot(2, 0, 12, 0.30m, 0.70m, 100, 1, assetId: "asset-b"),
                Snapshot(3, 0, 13, 0.35m, 0.65m, 101, 2, assetId: "asset-b")
            ]),
            new OrderBookProjector(),
            NullLogger<CollectorOrderBookIntegrityCoordinator>.Instance);

        var result = await coordinator.EvaluateAsync(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            3,
            [TokenId.Create("asset-a").Value, TokenId.Create("asset-b").Value],
            null,
            null,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("asset-a");
        result.Error.Message.Should().Contain("initial snapshot");
    }

    [Fact]
    public async Task EvaluateAsync_WithIssueAfterSourceReorder_ShouldLogArchiveProvenance()
    {
        var logger = new TestLogger();
        var coordinator = new CollectorOrderBookIntegrityCoordinator(
            new Reader(
            [
                Snapshot(1000, 0, 2000, 0.40m, 0.60m, 100, 1),
                PriceChanges(new ProjectionPriceChangeRecord(
                    1001, 0, 2001, "asset", 102, TradeSide.Sell, 0.61m, 10m,
                    "hash-2", 0.50m, 0.60m, 0, connectionEpoch: 1)),
                PriceChanges(new ProjectionPriceChangeRecord(
                    1002, 0, 2002, "asset", 101, TradeSide.Buy, 0.50m, 10m,
                    "hash-1", 0.50m, 0.60m, 0, connectionEpoch: 1)),
                PriceChanges(new ProjectionPriceChangeRecord(
                    3000, 7, 4000, "asset", 103, TradeSide.Buy, 0.45m, 10m,
                    "hash-3", 0.55m, 0.60m, 0, connectionEpoch: 1))
            ]),
            new OrderBookProjector(),
            logger);

        var result = await coordinator.EvaluateAsync(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            3,
            [TokenId.Create("asset").Value],
            null,
            null,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        logger.Entries.Should().ContainSingle();
        logger.Entries.Single().Should().Contain(new Dictionary<string, object?>
        {
            ["NormalizedEventId"] = 4000L,
            ["RawMessageId"] = 3000L,
            ["RawItemIndex"] = 7,
            ["SourceTimestamp"] = 103L,
            ["ConnectionEpoch"] = 1L
        });
    }

    [Fact]
    public async Task EvaluateAsync_WithEventOrderViolation_ShouldLogSafeEventProvenance()
    {
        var sessionId = CollectorSessionId.Create(Guid.NewGuid()).Value;
        var eventStartsAt = new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);
        var eventEndsAt = eventStartsAt.AddMinutes(5);
        var previousReceivedAt = eventStartsAt.AddSeconds(-1);
        var receivedAt = eventStartsAt.AddMinutes(2);
        var logger = new TestLogger();
        var coordinator = new CollectorOrderBookIntegrityCoordinator(
            new Reader(
            [
                Snapshot(
                    41,
                    2,
                    101,
                    bestBid: 0.40m,
                    bestAsk: 0.60m,
                    sourceTimestamp: 101,
                    connectionEpoch: 5,
                    receivedAt: eventStartsAt.AddSeconds(-2)),
                new NormalizedOrderBookEvent.BestBidAsk(new ProjectionBestBidAskRecord(
                    42,
                    3,
                    102,
                    "asset",
                    100,
                    0.40m,
                    0.60m,
                    0.20m,
                    connectionEpoch: 5,
                    receivedAt: previousReceivedAt)),
                Snapshot(
                    43,
                    4,
                    103,
                    bestBid: 0.40m,
                    bestAsk: 0.60m,
                    sourceTimestamp: 101,
                    connectionEpoch: 6,
                    receivedAt: receivedAt.AddMilliseconds(-1)),
                new NormalizedOrderBookEvent.BestBidAsk(new ProjectionBestBidAskRecord(
                    44,
                    5,
                    104,
                    "asset",
                    99,
                    0.40m,
                    0.60m,
                    0.20m,
                    connectionEpoch: 6,
                    receivedAt: receivedAt))
            ]),
            new OrderBookProjector(),
            logger);

        var result = await coordinator.EvaluateAsync(
            sessionId,
            3,
            [TokenId.Create("asset").Value],
            eventStartsAt,
            eventEndsAt,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        logger.Entries.Should().ContainSingle();
        logger.Entries.Single().Should().Contain(new Dictionary<string, object?>
        {
            ["SessionId"] = sessionId.Value,
            ["ProjectionVersion"] = 3,
            ["AssetId"] = "asset",
            ["EventType"] = "best_bid_ask",
            ["NormalizedEventId"] = 104L,
            ["RawMessageId"] = 44L,
            ["RawItemIndex"] = 5,
            ["SourceTimestamp"] = 99L,
            ["PreviousEventType"] = "best_bid_ask",
            ["PreviousNormalizedEventId"] = 102L,
            ["PreviousRawMessageId"] = 42L,
            ["PreviousRawItemIndex"] = 3,
            ["PreviousSourceTimestamp"] = 100L,
            ["RegressionMilliseconds"] = 1L,
            ["ConnectionEpoch"] = 6L,
            ["PreviousConnectionEpoch"] = 5L,
            ["ReceivedAt"] = receivedAt,
            ["PreviousReceivedAt"] = previousReceivedAt,
            ["WindowSegment"] = "TradingWindow"
        });
    }

    [Fact]
    public async Task EvaluateAsync_WithEmbeddedBestBidMismatch_ShouldLogSafeChangeProvenance()
    {
        var sessionId = CollectorSessionId.Create(Guid.NewGuid()).Value;
        var eventStartsAt = new DateTimeOffset(2026, 10, 3, 16, 40, 0, TimeSpan.Zero);
        var receivedAt = eventStartsAt.AddSeconds(-13);
        var logger = new TestLogger();
        var coordinator = new CollectorOrderBookIntegrityCoordinator(
            new Reader(
            [
                Snapshot(
                    1258360, 0, 1118317, 0.50m, 0.52m,
                    sourceTimestamp: 1791045547300,
                    connectionEpoch: 1,
                    receivedAt: receivedAt.AddMilliseconds(-10)),
                new NormalizedOrderBookEvent.BestBidAsk(new ProjectionBestBidAskRecord(
                    1258361, 0, 1118318, "asset", 1791045547353,
                    0.50m, 0.52m, 0.02m, 1, receivedAt.AddMilliseconds(-1))),
                PriceChanges(new ProjectionPriceChangeRecord(
                    1258362, 0, 1118319, "asset", 1791045547353,
                    TradeSide.Buy, 0.47m, 25m, "hash", 0.51m, 0.52m, 0,
                    1, receivedAt))
            ]),
            new OrderBookProjector(),
            logger);

        var result = await coordinator.EvaluateAsync(
            sessionId,
            3,
            [TokenId.Create("asset").Value],
            eventStartsAt,
            eventStartsAt.AddMinutes(5),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        logger.Entries.Should().ContainSingle();
        logger.Entries.Single().Should().Contain(new Dictionary<string, object?>
        {
            ["IssueType"] = OrderBookIntegrityIssueType.BestBidMismatch,
            ["LocalBestBid"] = 0.50m,
            ["LocalBestAsk"] = 0.52m,
            ["LocalSpread"] = 0.02m,
            ["EventBestBid"] = 0.51m,
            ["EventBestAsk"] = 0.52m,
            ["PreviousEventBestBid"] = 0.50m,
            ["PreviousEventBestAsk"] = 0.52m,
            ["PreviousEventSpread"] = 0.02m,
            ["PriceChangeCount"] = 1,
            ["LastChangeItemIndex"] = 0,
            ["LastChangeSide"] = TradeSide.Buy,
            ["LastChangePrice"] = 0.47m,
            ["LastChangeSize"] = 25m,
            ["RegressionMilliseconds"] = null,
            ["WindowSegment"] = "Preparation"
        });
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
            null,
            null,
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
            null,
            null,
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
        decimal bestAsk,
        long? sourceTimestamp = 100,
        long? connectionEpoch = null,
        DateTimeOffset? receivedAt = null,
        string assetId = "asset") =>
        new NormalizedOrderBookEvent.BookSnapshot(new ProjectionBookSnapshotRecord(
            rawMessageId,
            rawItemIndex,
            eventId,
            assetId,
            "condition",
            sourceTimestamp,
            "hash",
            0.01m,
            [new BookLevelRecord(OrderBookSide.Bid, 0, bestBid, 10)],
            [new BookLevelRecord(OrderBookSide.Ask, 0, bestAsk, 10)],
            connectionEpoch,
            receivedAt));

    private static NormalizedOrderBookEvent SnapshotWithLevels(
        long rawMessageId,
        long eventId,
        long sourceTimestamp,
        long connectionEpoch,
        IReadOnlyCollection<BookLevelRecord> bids,
        IReadOnlyCollection<BookLevelRecord> asks) =>
        new NormalizedOrderBookEvent.BookSnapshot(new ProjectionBookSnapshotRecord(
            rawMessageId,
            0,
            eventId,
            "asset",
            "condition",
            sourceTimestamp,
            "hash",
            0.01m,
            bids,
            asks,
            connectionEpoch));

    private static NormalizedOrderBookEvent PriceChanges(
        params ProjectionPriceChangeRecord[] records) =>
        new NormalizedOrderBookEvent.PriceChanges(records);

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

    private sealed class TestLogger : ILogger<CollectorOrderBookIntegrityCoordinator>
    {
        public List<IReadOnlyDictionary<string, object?>> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel != LogLevel.Error
                || state is not IEnumerable<KeyValuePair<string, object?>> properties)
            {
                return;
            }

            Entries.Add(properties.ToDictionary(item => item.Key, item => item.Value));
        }
    }
}
