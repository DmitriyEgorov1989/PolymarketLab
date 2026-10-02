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
            null,
            null,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.order_book.integrity.issue");
        result.Error.Message.Should().NotContain("Local best bid");
        projector.CallCount.Should().Be(2);
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
                    connectionEpoch: 6,
                    receivedAt: previousReceivedAt)),
                new NormalizedOrderBookEvent.BestBidAsk(new ProjectionBestBidAskRecord(
                    43,
                    4,
                    103,
                    "asset",
                    99,
                    0.40m,
                    0.60m,
                    0.20m,
                    connectionEpoch: 7,
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
            ["NormalizedEventId"] = 103L,
            ["RawMessageId"] = 43L,
            ["RawItemIndex"] = 4,
            ["SourceTimestamp"] = 99L,
            ["PreviousEventType"] = "best_bid_ask",
            ["PreviousNormalizedEventId"] = 102L,
            ["PreviousRawMessageId"] = 42L,
            ["PreviousRawItemIndex"] = 3,
            ["PreviousSourceTimestamp"] = 100L,
            ["RegressionMilliseconds"] = 1L,
            ["ConnectionEpoch"] = 7L,
            ["PreviousConnectionEpoch"] = 6L,
            ["ReceivedAt"] = receivedAt,
            ["PreviousReceivedAt"] = previousReceivedAt,
            ["WindowSegment"] = "TradingWindow"
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
        DateTimeOffset? receivedAt = null) =>
        new NormalizedOrderBookEvent.BookSnapshot(new ProjectionBookSnapshotRecord(
            rawMessageId,
            rawItemIndex,
            eventId,
            "asset",
            "condition",
            sourceTimestamp,
            "hash",
            0.01m,
            [new BookLevelRecord(OrderBookSide.Bid, 0, bestBid, 10)],
            [new BookLevelRecord(OrderBookSide.Ask, 0, bestAsk, 10)],
            connectionEpoch,
            receivedAt));

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
