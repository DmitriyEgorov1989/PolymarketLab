namespace PolymarketLab.DataCollection.Core.Application.Normalization.Models;

/// <summary>Предметные данные последней сделки по активу рынка.</summary>
public sealed record LastTradeRecord : NormalizedRecord
{
    /// <summary>Создаёт нормализованную запись последней сделки.</summary>
    /// <param name="price">Цена сделки в диапазоне от нуля до единицы.</param>
    /// <param name="size">Размер сделки или <see langword="null" />, если поле отсутствует.</param>
    /// <param name="side">Сторона сделки.</param>
    /// <param name="feeRateBps">Комиссия в базисных пунктах или <see langword="null" />.</param>
    /// <param name="transactionHash">Хеш транзакции или <see langword="null" />.</param>
    public LastTradeRecord(
        decimal price,
        decimal? size,
        TradeSide side,
        decimal? feeRateBps,
        string? transactionHash)
    {
        if (price is < 0 or > 1)
            throw new ArgumentOutOfRangeException(
                nameof(price),
                "Trade price must be between zero and one.");

        if (size < 0)
            throw new ArgumentOutOfRangeException(
                nameof(size),
                "Trade size cannot be negative.");

        if (!Enum.IsDefined(side))
            throw new ArgumentOutOfRangeException(nameof(side), "Trade side is not supported.");

        Price = price;
        Size = size;
        Side = side;
        FeeRateBps = feeRateBps;
        TransactionHash = transactionHash;
    }

    /// <summary>Цена сделки в диапазоне от нуля до единицы.</summary>
    public decimal Price { get; }

    /// <summary>Размер сделки или <see langword="null" />, если поле отсутствует.</summary>
    public decimal? Size { get; }

    /// <summary>Сторона сделки.</summary>
    public TradeSide Side { get; }

    /// <summary>Комиссия в базисных пунктах или <see langword="null" />, если поле отсутствует.</summary>
    public decimal? FeeRateBps { get; }

    /// <summary>Хеш транзакции или <see langword="null" />, если поле отсутствует.</summary>
    public string? TransactionHash { get; }
}
