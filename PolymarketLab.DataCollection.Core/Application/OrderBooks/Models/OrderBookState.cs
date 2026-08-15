using System.Collections.ObjectModel;
using BookSnapshotRecord = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models.BookSnapshotRecord;
using NormalizedBookLevelRecord = PolymarketLab.DataCollection.Core.Application.Normalization.Models.BookLevelRecord;

namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;

/// <summary>Текущее состояние стакана одного актива.</summary>
public sealed class OrderBookState
{
    private readonly SortedDictionary<decimal, OrderBookLevel> _bids = [];
    private readonly SortedDictionary<decimal, OrderBookLevel> _asks = [];

    /// <summary>Создаёт состояние, для которого полный снимок ещё не получен.</summary>
    /// <param name="assetId">Идентификатор актива, которому принадлежит состояние.</param>
    public OrderBookState(string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);

        AssetId = assetId;
        Bids = new ReadOnlyDictionary<decimal, OrderBookLevel>(_bids);
        Asks = new ReadOnlyDictionary<decimal, OrderBookLevel>(_asks);
        Status = OrderBookSyncStatus.Uninitialized;
    }

    /// <summary>Идентификатор актива, которому принадлежит состояние.</summary>
    public string AssetId { get; }

    /// <summary>Уровни покупки, индексированные по цене.</summary>
    public IReadOnlyDictionary<decimal, OrderBookLevel> Bids { get; }

    /// <summary>Уровни продажи, индексированные по цене.</summary>
    public IReadOnlyDictionary<decimal, OrderBookLevel> Asks { get; }

    /// <summary>Шаг цены из последнего полного снимка или <see langword="null" />.</summary>
    public decimal? TickSize { get; private set; }

    /// <summary>Epoch milliseconds последнего полного снимка или <see langword="null" />.</summary>
    public long? SourceTimestamp { get; private set; }

    /// <summary>Идентификатор последнего применённого нормализованного события.</summary>
    public long? NormalizedEventId { get; private set; }

    /// <summary>Максимальная цена покупки или <see langword="null" /> для пустой стороны.</summary>
    public decimal? BestBid { get; private set; }

    /// <summary>Минимальная цена продажи или <see langword="null" /> для пустой стороны.</summary>
    public decimal? BestAsk { get; private set; }

    /// <summary>Разница между лучшей ценой продажи и покупки или <see langword="null" />.</summary>
    public decimal? Spread { get; private set; }

    /// <summary>Обнаруженное нарушение целостности или <see langword="null" />.</summary>
    public OrderBookIntegrityIssue? IntegrityIssue { get; private set; }

    /// <summary>Степень доверия к актуальности состояния.</summary>
    public OrderBookSyncStatus Status { get; private set; }

    /// <summary>Полностью заменяет состояние данными нормализованного снимка стакана.</summary>
    /// <param name="book">Нормализованный полный снимок.</param>
    public void Apply(BookSnapshotRecord book)
    {
        ArgumentNullException.ThrowIfNull(book);
        if (!string.Equals(AssetId, book.AssetId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Snapshot asset id does not match the order book state.",
                nameof(book));
        }
        if (NormalizedEventId.HasValue && book.NormalizedEventId <= NormalizedEventId.Value)
        {
            throw new ArgumentException(
                "Snapshot event id must be greater than the last applied event id.",
                nameof(book));
        }

        var bids = BuildLevels(book.Bids, nameof(book));
        var asks = BuildLevels(book.Asks, nameof(book));

        _bids.Clear();
        _asks.Clear();
        AddLevels(_bids, bids);
        AddLevels(_asks, asks);

        TickSize = book.TickSize;
        SourceTimestamp = book.SourceTimestamp;
        NormalizedEventId = book.NormalizedEventId;
        BestBid = _bids.Count == 0 ? null : _bids.Last().Key;
        BestAsk = _asks.Count == 0 ? null : _asks.First().Key;
        Spread = BestBid.HasValue && BestAsk.HasValue
            ? BestAsk.Value - BestBid.Value
            : null;
        IntegrityIssue = BestBid.HasValue
            && BestAsk.HasValue
            && BestBid.Value > BestAsk.Value
            ? OrderBookIntegrityIssue.CrossedBook
            : null;
        Status = IntegrityIssue.HasValue
            ? OrderBookSyncStatus.Suspect
            : OrderBookSyncStatus.Synchronized;
    }

    private static SortedDictionary<decimal, OrderBookLevel> BuildLevels(
        IEnumerable<NormalizedBookLevelRecord> source,
        string parameterName)
    {
        var levels = new SortedDictionary<decimal, OrderBookLevel>();
        foreach (var sourceLevel in source)
        {
            var level = new OrderBookLevel(sourceLevel.Price, sourceLevel.Size);
            if (!levels.TryAdd(level.Price, level))
            {
                throw new ArgumentException(
                    $"Snapshot contains duplicate price '{level.Price}'.",
                    parameterName);
            }
        }

        return levels;
    }

    private static void AddLevels(
        SortedDictionary<decimal, OrderBookLevel> target,
        IEnumerable<KeyValuePair<decimal, OrderBookLevel>> source)
    {
        foreach (var level in source)
            target.Add(level.Key, level.Value);
    }
}
