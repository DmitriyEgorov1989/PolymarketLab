namespace PolymarketLab.DataCollection.Core.Application.Normalization.Models;

/// <summary>Изменение цены одного актива внутри события <c>price_change</c>.</summary>
public sealed record PriceChangeRecord : NormalizedRecord
{
    /// <summary>Создаёт нормализованную запись изменения цены.</summary>
    /// <param name="itemIndex">Позиция элемента внутри массива <c>price_changes</c>.</param>
    /// <param name="assetId">Идентификатор актива.</param>
    /// <param name="price">Новая цена в диапазоне от нуля до единицы.</param>
    /// <param name="size">Размер изменения.</param>
    /// <param name="side">Сторона изменения.</param>
    /// <param name="hash">Внешний hash или <see langword="null" />.</param>
    /// <param name="bestBid">Лучшая цена покупки или <see langword="null" />.</param>
    /// <param name="bestAsk">Лучшая цена продажи или <see langword="null" />.</param>
    public PriceChangeRecord(
        int itemIndex,
        string assetId,
        decimal price,
        decimal size,
        TradeSide side,
        string? hash,
        decimal? bestBid,
        decimal? bestAsk)
    {
        if (itemIndex < 0)
            throw new ArgumentOutOfRangeException(
                nameof(itemIndex),
                "Price change item index cannot be negative.");

        if (string.IsNullOrWhiteSpace(assetId))
            throw new ArgumentException("Asset id is required.", nameof(assetId));

        if (price is < 0 or > 1)
            throw new ArgumentOutOfRangeException(
                nameof(price),
                "Price must be between zero and one.");

        if (size < 0)
            throw new ArgumentOutOfRangeException(
                nameof(size),
                "Size cannot be negative.");

        if (!Enum.IsDefined(side))
            throw new ArgumentOutOfRangeException(nameof(side), "Trade side is not supported.");

        if (bestBid is < 0 or > 1)
            throw new ArgumentOutOfRangeException(
                nameof(bestBid),
                "Best bid must be between zero and one.");

        if (bestAsk is < 0 or > 1)
            throw new ArgumentOutOfRangeException(
                nameof(bestAsk),
                "Best ask must be between zero and one.");

        ItemIndex = itemIndex;
        AssetId = assetId;
        Price = price;
        Size = size;
        Side = side;
        Hash = hash;
        BestBid = bestBid;
        BestAsk = bestAsk;
    }

    /// <summary>Позиция элемента внутри исходного массива <c>price_changes</c>.</summary>
    public int ItemIndex { get; }

    /// <summary>Идентификатор актива.</summary>
    public string AssetId { get; }

    /// <summary>Новая цена в диапазоне от нуля до единицы.</summary>
    public decimal Price { get; }

    /// <summary>Неотрицательный размер изменения.</summary>
    public decimal Size { get; }

    /// <summary>Сторона изменения.</summary>
    public TradeSide Side { get; }

    /// <summary>Внешний hash или <see langword="null" />, если поле отсутствует.</summary>
    public string? Hash { get; }

    /// <summary>Лучшая цена покупки или <see langword="null" />, если поле отсутствует.</summary>
    public decimal? BestBid { get; }

    /// <summary>Лучшая цена продажи или <see langword="null" />, если поле отсутствует.</summary>
    public decimal? BestAsk { get; }
}
