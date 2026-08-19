namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;

/// <summary>Полный снимок стакана, полученный из внешнего источника.</summary>
public sealed record OrderBookSnapshot
{
    /// <summary>Создаёт проверенный полный снимок внешнего стакана.</summary>
    /// <param name="marketConditionId">Идентификатор условия рынка.</param>
    /// <param name="assetId">Идентификатор актива.</param>
    /// <param name="sourceTimestamp">Внешнее время снимка в миллисекундах Unix epoch.</param>
    /// <param name="hash">Непрозрачный hash состояния, рассчитанный внешним источником.</param>
    /// <param name="bids">Полный набор уровней покупки.</param>
    /// <param name="asks">Полный набор уровней продажи.</param>
    /// <param name="minimumOrderSize">Минимальный допустимый размер заявки.</param>
    /// <param name="tickSize">Текущий шаг цены.</param>
    /// <param name="negativeRisk">Признак модели negative risk внешнего рынка.</param>
    /// <param name="lastTradePrice">Цена последней сделки в диапазоне от нуля до единицы.</param>
    public OrderBookSnapshot(
        string marketConditionId,
        string assetId,
        long sourceTimestamp,
        string hash,
        IReadOnlyCollection<OrderBookSnapshotLevel> bids,
        IReadOnlyCollection<OrderBookSnapshotLevel> asks,
        decimal minimumOrderSize,
        decimal tickSize,
        bool negativeRisk,
        decimal lastTradePrice)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marketConditionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        ArgumentNullException.ThrowIfNull(bids);
        ArgumentNullException.ThrowIfNull(asks);

        if (sourceTimestamp < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceTimestamp));
        if (minimumOrderSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumOrderSize));
        if (tickSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(tickSize));
        if (lastTradePrice is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(lastTradePrice));

        MarketConditionId = marketConditionId;
        AssetId = assetId;
        SourceTimestamp = sourceTimestamp;
        Hash = hash;
        Bids = bids.ToArray();
        Asks = asks.ToArray();
        MinimumOrderSize = minimumOrderSize;
        TickSize = tickSize;
        NegativeRisk = negativeRisk;
        LastTradePrice = lastTradePrice;
    }

    /// <summary>Идентификатор условия рынка.</summary>
    public string MarketConditionId { get; }

    /// <summary>Идентификатор актива, которому принадлежит стакан.</summary>
    public string AssetId { get; }

    /// <summary>Внешнее время снимка в миллисекундах Unix epoch.</summary>
    public long SourceTimestamp { get; }

    /// <summary>Непрозрачный hash, предоставленный внешним источником.</summary>
    public string Hash { get; }

    /// <summary>Полный набор уровней покупки.</summary>
    public IReadOnlyList<OrderBookSnapshotLevel> Bids { get; }

    /// <summary>Полный набор уровней продажи.</summary>
    public IReadOnlyList<OrderBookSnapshotLevel> Asks { get; }

    /// <summary>Минимальный допустимый размер заявки.</summary>
    public decimal MinimumOrderSize { get; }

    /// <summary>Текущий шаг цены.</summary>
    public decimal TickSize { get; }

    /// <summary>Признак модели negative risk внешнего рынка.</summary>
    public bool NegativeRisk { get; }

    /// <summary>Цена последней сделки в диапазоне от нуля до единицы.</summary>
    public decimal LastTradePrice { get; }
}

/// <summary>Агрегированный ценовой уровень внешнего снимка стакана.</summary>
public readonly record struct OrderBookSnapshotLevel
{
    /// <summary>Создаёт один агрегированный уровень внешнего снимка.</summary>
    /// <param name="price">Цена в диапазоне от нуля до единицы.</param>
    /// <param name="size">Неотрицательный суммарный размер заявок.</param>
    public OrderBookSnapshotLevel(decimal price, decimal size)
    {
        if (price is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(price));
        if (size < 0)
            throw new ArgumentOutOfRangeException(nameof(size));

        Price = price;
        Size = size;
    }

    /// <summary>Цена уровня в диапазоне от нуля до единицы.</summary>
    public decimal Price { get; }

    /// <summary>Неотрицательный суммарный размер заявок.</summary>
    public decimal Size { get; }
}
