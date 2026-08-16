using TradeSide = PolymarketLab.DataCollection.Core.Application.Normalization.Models.TradeSide;

namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;

/// <summary>Нормализованное изменение уровня стакана для последовательного применения Projector.</summary>
public sealed record PriceChangeRecord
{
    /// <summary>Создаёт входную модель изменения одного уровня стакана.</summary>
    /// <param name="rawMessageId">Идентификатор исходного сообщения в архиве.</param>
    /// <param name="rawItemIndex">Позиция логического события внутри исходного сообщения.</param>
    /// <param name="normalizedEventId">Идентификатор сохранённого нормализованного события.</param>
    /// <param name="assetId">Идентификатор актива.</param>
    /// <param name="sourceTimestamp">Epoch milliseconds из исходного события или <see langword="null" />.</param>
    /// <param name="side">Сторона изменения.</param>
    /// <param name="price">Цена изменяемого уровня.</param>
    /// <param name="size">Новый размер уровня.</param>
    /// <param name="hash">Внешний hash или <see langword="null" />.</param>
    /// <param name="bestBid">Лучшая цена покупки или <see langword="null" />.</param>
    /// <param name="bestAsk">Лучшая цена продажи или <see langword="null" />.</param>
    /// <param name="itemIndex">Позиция изменения внутри исходного события.</param>
    public PriceChangeRecord(
        long rawMessageId,
        int rawItemIndex,
        long normalizedEventId,
        string assetId,
        long? sourceTimestamp,
        TradeSide side,
        decimal price,
        decimal size,
        string? hash,
        decimal? bestBid,
        decimal? bestAsk,
        int itemIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);

        if (!Enum.IsDefined(side))
            throw new ArgumentOutOfRangeException(nameof(side), "Trade side is not supported.");
        if (price is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(price), "Price must be between zero and one.");
        if (size < 0)
            throw new ArgumentOutOfRangeException(nameof(size), "Size cannot be negative.");
        if (bestBid is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(bestBid), "Best bid must be between zero and one.");
        if (bestAsk is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(bestAsk), "Best ask must be between zero and one.");
        if (itemIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(itemIndex), "Item index cannot be negative.");

        Position = new OrderBookEventPosition(rawMessageId, rawItemIndex, normalizedEventId);
        AssetId = assetId;
        SourceTimestamp = sourceTimestamp;
        Side = side;
        Price = price;
        Size = size;
        Hash = hash;
        BestBid = bestBid;
        BestAsk = bestAsk;
        ItemIndex = itemIndex;
    }

    /// <summary>Позиция события в нормализованном архиве.</summary>
    public OrderBookEventPosition Position { get; }

    /// <summary>Идентификатор сохранённого нормализованного события.</summary>
    public long NormalizedEventId => Position.NormalizedEventId;

    /// <summary>Идентификатор актива.</summary>
    public string AssetId { get; }

    /// <summary>Epoch milliseconds из исходного события или <see langword="null" />.</summary>
    public long? SourceTimestamp { get; }

    /// <summary>Сторона изменения.</summary>
    public TradeSide Side { get; }

    /// <summary>Цена изменяемого уровня в диапазоне от нуля до единицы.</summary>
    public decimal Price { get; }

    /// <summary>Неотрицательный размер изменения.</summary>
    public decimal Size { get; }

    /// <summary>Внешний hash или <see langword="null" />, если поле отсутствовало.</summary>
    public string? Hash { get; }

    /// <summary>Лучшая цена покупки или <see langword="null" />, если поле отсутствовало.</summary>
    public decimal? BestBid { get; }

    /// <summary>Лучшая цена продажи или <see langword="null" />, если поле отсутствовало.</summary>
    public decimal? BestAsk { get; }

    /// <summary>Позиция изменения внутри массива <c>price_changes</c>.</summary>
    public int ItemIndex { get; }
}
