using NormalizedBookLevelRecord = PolymarketLab.DataCollection.Core.Application.Normalization.Models.BookLevelRecord;
using OrderBookSide = PolymarketLab.DataCollection.Core.Application.Normalization.Models.OrderBookSide;

namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;

/// <summary>Нормализованный снимок стакана для последовательного применения Projector.</summary>
public sealed record BookSnapshotRecord
{
    /// <summary>Создаёт входную модель полного снимка стакана.</summary>
    /// <param name="normalizedEventId">Идентификатор сохранённого нормализованного события.</param>
    /// <param name="assetId">Идентификатор актива.</param>
    /// <param name="marketConditionId">Идентификатор условия рынка.</param>
    /// <param name="sourceTimestamp">Epoch milliseconds из исходного события или <see langword="null" />.</param>
    /// <param name="hash">Внешний hash снимка.</param>
    /// <param name="tickSize">Шаг цены или <see langword="null" />, если поле отсутствовало.</param>
    /// <param name="bids">Уровни стороны покупки в исходном порядке.</param>
    /// <param name="asks">Уровни стороны продажи в исходном порядке.</param>
    public BookSnapshotRecord(
        long normalizedEventId,
        string assetId,
        string marketConditionId,
        long? sourceTimestamp,
        string hash,
        decimal? tickSize,
        IReadOnlyCollection<NormalizedBookLevelRecord> bids,
        IReadOnlyCollection<NormalizedBookLevelRecord> asks)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(normalizedEventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(marketConditionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        ArgumentNullException.ThrowIfNull(bids);
        ArgumentNullException.ThrowIfNull(asks);

        if (tickSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(tickSize), "Tick size must be positive.");
        if (bids.Any(level => level.Side != OrderBookSide.Bid))
            throw new ArgumentException("Bids must contain only bid levels.", nameof(bids));
        if (asks.Any(level => level.Side != OrderBookSide.Ask))
            throw new ArgumentException("Asks must contain only ask levels.", nameof(asks));

        NormalizedEventId = normalizedEventId;
        AssetId = assetId;
        MarketConditionId = marketConditionId;
        SourceTimestamp = sourceTimestamp;
        Hash = hash;
        TickSize = tickSize;
        Bids = bids.ToArray();
        Asks = asks.ToArray();
    }

    /// <summary>Идентификатор сохранённого нормализованного события.</summary>
    public long NormalizedEventId { get; }

    /// <summary>Идентификатор актива, к которому относится стакан.</summary>
    public string AssetId { get; }

    /// <summary>Идентификатор условия рынка.</summary>
    public string MarketConditionId { get; }

    /// <summary>Epoch milliseconds из исходного события или <see langword="null" />.</summary>
    public long? SourceTimestamp { get; }

    /// <summary>Внешний hash снимка.</summary>
    public string Hash { get; }

    /// <summary>Шаг цены или <see langword="null" />, если поле отсутствовало.</summary>
    public decimal? TickSize { get; }

    /// <summary>Уровни стороны покупки в исходном порядке.</summary>
    public IReadOnlyList<NormalizedBookLevelRecord> Bids { get; }

    /// <summary>Уровни стороны продажи в исходном порядке.</summary>
    public IReadOnlyList<NormalizedBookLevelRecord> Asks { get; }
}
