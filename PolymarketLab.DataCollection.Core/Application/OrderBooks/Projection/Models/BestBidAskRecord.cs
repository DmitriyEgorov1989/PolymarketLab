namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;

/// <summary>Нормализованные лучшие цены для последовательного применения Projector.</summary>
public sealed record BestBidAskRecord
{
    /// <summary>Создаёт входную модель лучших цен актива.</summary>
    /// <param name="rawMessageId">Идентификатор исходного сообщения в архиве.</param>
    /// <param name="rawItemIndex">Позиция логического события внутри исходного сообщения.</param>
    /// <param name="normalizedEventId">Идентификатор сохранённого нормализованного события.</param>
    /// <param name="assetId">Идентификатор актива.</param>
    /// <param name="sourceTimestamp">Epoch milliseconds из исходного события или <see langword="null" />.</param>
    /// <param name="bestBid">Лучшая цена покупки или <see langword="null" /> для пустой стороны.</param>
    /// <param name="bestAsk">Лучшая цена продажи или <see langword="null" /> для пустой стороны.</param>
    /// <param name="spread">Спред или <see langword="null" />, если одна из сторон пуста.</param>
    public BestBidAskRecord(
        long rawMessageId,
        int rawItemIndex,
        long normalizedEventId,
        string assetId,
        long? sourceTimestamp,
        decimal? bestBid,
        decimal? bestAsk,
        decimal? spread)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);

        if (bestBid is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(bestBid), "Best bid must be between zero and one.");
        if (bestAsk is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(bestAsk), "Best ask must be between zero and one.");
        if (spread < 0)
            throw new ArgumentOutOfRangeException(nameof(spread), "Spread cannot be negative.");

        Position = new OrderBookEventPosition(rawMessageId, rawItemIndex, normalizedEventId);
        AssetId = assetId;
        SourceTimestamp = sourceTimestamp;
        BestBid = bestBid;
        BestAsk = bestAsk;
        Spread = spread;
    }

    /// <summary>Позиция события в нормализованном архиве.</summary>
    public OrderBookEventPosition Position { get; }

    /// <summary>Идентификатор сохранённого нормализованного события.</summary>
    public long NormalizedEventId => Position.NormalizedEventId;

    /// <summary>Идентификатор актива.</summary>
    public string AssetId { get; }

    /// <summary>Epoch milliseconds из исходного события или <see langword="null" />.</summary>
    public long? SourceTimestamp { get; }

    /// <summary>Лучшая цена покупки или <see langword="null" /> для пустой стороны.</summary>
    public decimal? BestBid { get; }

    /// <summary>Лучшая цена продажи или <see langword="null" /> для пустой стороны.</summary>
    public decimal? BestAsk { get; }

    /// <summary>Спред из нормализованной проекции или <see langword="null" />.</summary>
    public decimal? Spread { get; }
}
