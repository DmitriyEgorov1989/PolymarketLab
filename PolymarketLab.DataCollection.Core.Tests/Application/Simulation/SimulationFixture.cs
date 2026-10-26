using System.Collections.Immutable;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;
using PolymarketLab.DataCollection.Core.Application.Simulation;
using PolymarketLab.DataCollection.Core.Application.Simulation.Datasets;
using Book = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models.BookSnapshotRecord;
using Change = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models.PriceChangeRecord;

namespace PolymarketLab.DataCollection.Core.Tests.Application.Simulation;

internal static class SimulationFixture
{
    public static readonly DateTimeOffset Start = new(2026, 10, 16, 9, 20, 0, TimeSpan.Zero);
    public static readonly Guid SessionId = Guid.Parse("33a17de5-2b29-4de2-929c-2d1c0ee46588");

    public static NormalizedOrderBookEvent Book(long id, int item, string asset,
        DateTimeOffset at, long epoch = 1, decimal? tick = 0.01m,
        decimal ask = 0.48m, decimal size = 10m, long? source = null) =>
        new NormalizedOrderBookEvent.BookSnapshot(new Book(id, item, id * 10 + item,
            asset, "condition", source ?? id * 1000, "hash", tick,
            [new BookLevelRecord(OrderBookSide.Bid, 0, 0.40m, size)],
            [new BookLevelRecord(OrderBookSide.Ask, 0, ask, size)], epoch, at));

    public static NormalizedOrderBookEvent Change(long id, string asset, DateTimeOffset at,
        decimal price, decimal size, long epoch = 1, long? source = null) =>
        new NormalizedOrderBookEvent.PriceChanges([new Change(id, 0, id * 10,
            asset, source ?? id * 1000, TradeSide.Sell, price, size,
            "hash", null, null, 0, epoch, at)]);

    public static ReplayRawMessage Message(long id, DateTimeOffset at, long epoch = 1,
        params NormalizedOrderBookEvent[] events) =>
        new(id, epoch, at, SimulationJson.Hash(new { id, at, epoch }), events.ToImmutableArray());

    public static ReplayRawMessage Initial() => Message(1, Start.AddSeconds(-1), 1,
        Book(1, 0, "up", Start.AddSeconds(-1)), Book(1, 1, "down", Start.AddSeconds(-1)));

    public static ExperimentDataset Dataset(params ReplayRawMessage[] messages) =>
        ExperimentDataset.Create(SessionId, 1, "condition", Start, Start.AddMinutes(5),
            [new ReplayAsset("up", "Up", 0), new ReplayAsset("down", "Down", 1)],
            messages.Length == 0 ? [Initial()] : messages).Value;
}
