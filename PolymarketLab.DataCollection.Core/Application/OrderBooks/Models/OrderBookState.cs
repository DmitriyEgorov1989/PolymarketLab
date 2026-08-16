using System.Collections.ObjectModel;
using System.Globalization;
using BestBidAskRecord = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models.BestBidAskRecord;
using BookSnapshotRecord = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models.BookSnapshotRecord;
using OrderBookEventPosition = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models.OrderBookEventPosition;
using PriceChangeRecord = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models.PriceChangeRecord;
using TickSizeChangeRecord = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models.TickSizeChangeRecord;
using NormalizedBookLevelRecord = PolymarketLab.DataCollection.Core.Application.Normalization.Models.BookLevelRecord;
using TradeSide = PolymarketLab.DataCollection.Core.Application.Normalization.Models.TradeSide;

namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;

/// <summary>Текущее состояние стакана одного актива.</summary>
public sealed class OrderBookState
{
    private readonly SortedDictionary<decimal, OrderBookLevel> _bids = [];
    private readonly SortedDictionary<decimal, OrderBookLevel> _asks = [];
    private readonly TimeProvider _timeProvider;
    private long? _lastKnownSourceTimestamp;

    /// <summary>Создаёт состояние, для которого полный снимок ещё не получен.</summary>
    /// <param name="assetId">Идентификатор актива, которому принадлежит состояние.</param>
    /// <param name="timeProvider">Источник времени обнаружения проблем целостности.</param>
    public OrderBookState(string assetId, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);

        AssetId = assetId;
        _timeProvider = timeProvider ?? TimeProvider.System;
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

    /// <summary>Последний известный шаг цены или <see langword="null" />.</summary>
    public decimal? TickSize { get; private set; }

    /// <summary>Epoch milliseconds последнего применённого события или <see langword="null" />.</summary>
    public long? SourceTimestamp { get; private set; }

    /// <summary>Идентификатор последнего применённого нормализованного события.</summary>
    public long? NormalizedEventId { get; private set; }

    /// <summary>Позиция последнего применённого события в нормализованном архиве.</summary>
    public OrderBookEventPosition? EventPosition { get; private set; }

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
        EnsureIncreasingPosition(book.Position, nameof(book), "Snapshot");

        var bids = BuildLevels(book.Bids, nameof(book));
        var asks = BuildLevels(book.Asks, nameof(book));
        if (!AcceptSourceTimestamp(book.SourceTimestamp, book.NormalizedEventId))
            return;

        _bids.Clear();
        _asks.Clear();
        AddLevels(_bids, bids);
        AddLevels(_asks, asks);

        TickSize = book.TickSize;
        CommitEvent(book.Position, book.SourceTimestamp);
        RecalculateDerivedState(preserveIntegrityMismatch: false);
    }

    /// <summary>Атомарно применяет изменения уровней одного нормализованного события.</summary>
    /// <param name="changes">Изменения уровней, принадлежащие одному событию.</param>
    public void Apply(IReadOnlyCollection<PriceChangeRecord> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0)
            throw new ArgumentException("Price change group cannot be empty.", nameof(changes));
        if (Status == OrderBookSyncStatus.Uninitialized)
        {
            throw new InvalidOperationException(
                "A full snapshot must be applied before price changes.");
        }

        var validatedChanges = new List<PriceChangeRecord>(changes.Count);
        foreach (var change in changes)
        {
            if (change is null)
            {
                throw new ArgumentException(
                    "Price change group cannot contain null elements.",
                    nameof(changes));
            }

            validatedChanges.Add(change);
        }

        var orderedChanges = validatedChanges.OrderBy(change => change.ItemIndex).ToArray();
        var first = orderedChanges[0];

        if (orderedChanges.Any(change => !string.Equals(
                AssetId,
                change.AssetId,
                StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "Price change asset id does not match the order book state.",
                nameof(changes));
        }
        if (orderedChanges.Any(change => change.Position != first.Position))
        {
            throw new ArgumentException(
                "Price changes must belong to the same archive event.",
                nameof(changes));
        }
        if (orderedChanges.Any(change => change.SourceTimestamp != first.SourceTimestamp))
        {
            throw new ArgumentException(
                "Price changes must have the same source timestamp.",
                nameof(changes));
        }
        EnsureIncreasingPosition(first.Position, nameof(changes), "Price change");
        if (orderedChanges.Select(change => change.ItemIndex).Distinct().Count() != orderedChanges.Length)
        {
            throw new ArgumentException(
                "Price change group contains duplicate item indexes.",
                nameof(changes));
        }
        if (!AcceptSourceTimestamp(first.SourceTimestamp, first.NormalizedEventId))
            return;

        foreach (var change in orderedChanges)
        {
            var levels = change.Side switch
            {
                TradeSide.Buy => _bids,
                TradeSide.Sell => _asks,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(changes),
                    "Price change side is not supported.")
            };

            if (change.Size == 0)
                levels.Remove(change.Price);
            else
                levels[change.Price] = new OrderBookLevel(change.Price, change.Size);
        }

        CommitEvent(first.Position, first.SourceTimestamp);
        RecalculateDerivedState();
    }

    /// <summary>Применяет изменение шага цены, не изменяя существующие уровни.</summary>
    /// <param name="change">Нормализованное изменение шага цены.</param>
    public void Apply(TickSizeChangeRecord change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (Status == OrderBookSyncStatus.Uninitialized || !TickSize.HasValue)
        {
            throw new InvalidOperationException(
                "A full snapshot with a tick size must be applied before tick size changes.");
        }
        if (!string.Equals(AssetId, change.AssetId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Tick size change asset id does not match the order book state.",
                nameof(change));
        }
        EnsureIncreasingPosition(change.Position, nameof(change), "Tick size change");
        if (!AcceptSourceTimestamp(change.SourceTimestamp, change.NormalizedEventId))
            return;

        CommitEvent(change.Position, change.SourceTimestamp);

        if (TickSize.Value != change.OldTickSize)
        {
            IntegrityIssue = CreateIssue(
                OrderBookIntegrityIssueType.TickSizeMismatch,
                $"Local tick size '{Format(TickSize)}' does not match event old tick size '{Format(change.OldTickSize)}'.");
            Status = OrderBookSyncStatus.Suspect;
            return;
        }

        TickSize = change.NewTickSize;
        RecalculateDerivedState();
    }

    /// <summary>Сверяет вычисленные лучшие цены и спред с нормализованным событием.</summary>
    /// <param name="quote">Нормализованные лучшие цены актива.</param>
    public void Apply(BestBidAskRecord quote)
    {
        ArgumentNullException.ThrowIfNull(quote);
        if (Status == OrderBookSyncStatus.Uninitialized)
        {
            throw new InvalidOperationException(
                "A full snapshot must be applied before best bid and ask checks.");
        }
        if (!string.Equals(AssetId, quote.AssetId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Best bid and ask asset id does not match the order book state.",
                nameof(quote));
        }
        EnsureIncreasingPosition(quote.Position, nameof(quote), "Best bid and ask");
        if (!AcceptSourceTimestamp(quote.SourceTimestamp, quote.NormalizedEventId))
            return;

        CommitEvent(quote.Position, quote.SourceTimestamp);

        var issue = FindBestBidAskMismatch(quote);
        if (issue is null)
            return;

        IntegrityIssue = issue;
        Status = OrderBookSyncStatus.Suspect;
    }

    private void RecalculateDerivedState(bool preserveIntegrityMismatch = true)
    {
        BestBid = _bids.Count == 0 ? null : _bids.Last().Key;
        BestAsk = _asks.Count == 0 ? null : _asks.First().Key;
        Spread = BestBid.HasValue && BestAsk.HasValue
            ? BestAsk.Value - BestBid.Value
            : null;
        var isCrossed = BestBid.HasValue
            && BestAsk.HasValue
            && BestBid.Value > BestAsk.Value;
        IntegrityIssue = preserveIntegrityMismatch
            && IsPersistentMismatch(IntegrityIssue)
            ? IntegrityIssue
            : isCrossed
            ? IntegrityIssue?.Type == OrderBookIntegrityIssueType.CrossedBook
                ? IntegrityIssue
                : CreateIssue(
                    OrderBookIntegrityIssueType.CrossedBook,
                    $"Local best bid '{Format(BestBid)}' is greater than local best ask '{Format(BestAsk)}'.")
            : null;
        Status = IntegrityIssue is not null
            ? OrderBookSyncStatus.Suspect
            : OrderBookSyncStatus.Synchronized;
    }

    private OrderBookIntegrityIssue? FindBestBidAskMismatch(BestBidAskRecord quote)
    {
        if (BestBid != quote.BestBid)
        {
            return CreateIssue(
                OrderBookIntegrityIssueType.BestBidMismatch,
                $"Local best bid '{Format(BestBid)}' does not match event best bid '{Format(quote.BestBid)}'.");
        }
        if (BestAsk != quote.BestAsk)
        {
            return CreateIssue(
                OrderBookIntegrityIssueType.BestAskMismatch,
                $"Local best ask '{Format(BestAsk)}' does not match event best ask '{Format(quote.BestAsk)}'.");
        }
        if (Spread != quote.Spread)
        {
            return CreateIssue(
                OrderBookIntegrityIssueType.SpreadMismatch,
                $"Local spread '{Format(Spread)}' does not match event spread '{Format(quote.Spread)}'.");
        }

        return null;
    }

    private OrderBookIntegrityIssue CreateIssue(
        OrderBookIntegrityIssueType type,
        string message,
        long? normalizedEventId = null)
    {
        return new OrderBookIntegrityIssue(
            type,
            message,
            normalizedEventId ?? NormalizedEventId,
            _timeProvider.GetUtcNow());
    }

    private void EnsureIncreasingPosition(
        OrderBookEventPosition position,
        string parameterName,
        string eventName)
    {
        if (EventPosition is not null && position.CompareTo(EventPosition) <= 0)
        {
            throw new ArgumentException(
                $"{eventName} archive position must be greater than the last applied position.",
                parameterName);
        }
    }

    private bool AcceptSourceTimestamp(long? sourceTimestamp, long normalizedEventId)
    {
        if (sourceTimestamp.HasValue
            && _lastKnownSourceTimestamp.HasValue
            && sourceTimestamp.Value < _lastKnownSourceTimestamp.Value)
        {
            IntegrityIssue = CreateIssue(
                OrderBookIntegrityIssueType.EventOrderViolation,
                $"Event source timestamp '{sourceTimestamp.Value}' is less than the last known timestamp '{_lastKnownSourceTimestamp.Value}'.",
                normalizedEventId);
            Status = OrderBookSyncStatus.Suspect;
            return false;
        }

        return true;
    }

    private void CommitEvent(OrderBookEventPosition position, long? sourceTimestamp)
    {
        EventPosition = position;
        SourceTimestamp = sourceTimestamp;
        NormalizedEventId = position.NormalizedEventId;
        if (sourceTimestamp.HasValue
            && (!_lastKnownSourceTimestamp.HasValue
                || sourceTimestamp.Value > _lastKnownSourceTimestamp.Value))
        {
            _lastKnownSourceTimestamp = sourceTimestamp.Value;
        }
    }

    private static bool IsPersistentMismatch(OrderBookIntegrityIssue? issue)
    {
        return issue?.Type is OrderBookIntegrityIssueType.BestBidMismatch
            or OrderBookIntegrityIssueType.BestAskMismatch
            or OrderBookIntegrityIssueType.SpreadMismatch
            or OrderBookIntegrityIssueType.TickSizeMismatch
            or OrderBookIntegrityIssueType.EventOrderViolation;
    }

    private static string Format(decimal? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? "null";
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
