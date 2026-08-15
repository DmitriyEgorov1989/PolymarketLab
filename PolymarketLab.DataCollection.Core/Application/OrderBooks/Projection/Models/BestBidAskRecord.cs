namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;

/// <summary>Нормализованные лучшие цены для последовательного применения Projector.</summary>
public sealed record BestBidAskRecord
{
    /// <summary>Создаёт входную модель лучших цен актива.</summary>
    /// <param name="normalizedEventId">Идентификатор сохранённого нормализованного события.</param>
    /// <param name="assetId">Идентификатор актива.</param>
    /// <param name="sourceTimestamp">Epoch milliseconds из исходного события или <see langword="null" />.</param>
    /// <param name="bestBid">Лучшая цена покупки.</param>
    /// <param name="bestAsk">Лучшая цена продажи.</param>
    /// <param name="spread">Спред из нормализованной проекции.</param>
    public BestBidAskRecord(
        long normalizedEventId,
        string assetId,
        long? sourceTimestamp,
        decimal bestBid,
        decimal bestAsk,
        decimal spread)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(normalizedEventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);

        if (bestBid is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(bestBid), "Best bid must be between zero and one.");
        if (bestAsk is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(bestAsk), "Best ask must be between zero and one.");
        if (spread < 0)
            throw new ArgumentOutOfRangeException(nameof(spread), "Spread cannot be negative.");

        NormalizedEventId = normalizedEventId;
        AssetId = assetId;
        SourceTimestamp = sourceTimestamp;
        BestBid = bestBid;
        BestAsk = bestAsk;
        Spread = spread;
    }

    /// <summary>Идентификатор сохранённого нормализованного события.</summary>
    public long NormalizedEventId { get; }

    /// <summary>Идентификатор актива.</summary>
    public string AssetId { get; }

    /// <summary>Epoch milliseconds из исходного события или <see langword="null" />.</summary>
    public long? SourceTimestamp { get; }

    /// <summary>Лучшая цена покупки.</summary>
    public decimal BestBid { get; }

    /// <summary>Лучшая цена продажи.</summary>
    public decimal BestAsk { get; }

    /// <summary>Спред, полученный из нормализованной проекции без пересчёта.</summary>
    public decimal Spread { get; }
}
