namespace PolymarketLab.DataCollection.Core.Application.Normalization.Models;

/// <summary>Метаданные снимка стакана.</summary>
public sealed record BookSnapshotRecord : NormalizedRecord
{
    /// <summary>Создаёт нормализованную запись снимка стакана.</summary>
    /// <param name="hash">Внешний hash снимка.</param>
    /// <param name="tickSize">Шаг цены или <see langword="null" />, если поле отсутствует.</param>
    /// <param name="lastTradePrice">Цена последней сделки или <see langword="null" />, если поле отсутствует.</param>
    public BookSnapshotRecord(
        string hash,
        decimal? tickSize,
        decimal? lastTradePrice)
    {
        if (string.IsNullOrWhiteSpace(hash))
            throw new ArgumentException("Snapshot hash is required.", nameof(hash));

        if (tickSize <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(tickSize),
                "Tick size must be positive.");

        if (lastTradePrice is < 0 or > 1)
            throw new ArgumentOutOfRangeException(
                nameof(lastTradePrice),
                "Last trade price must be between zero and one.");

        Hash = hash;
        TickSize = tickSize;
        LastTradePrice = lastTradePrice;
    }

    /// <summary>Внешний hash снимка.</summary>
    public string Hash { get; }

    /// <summary>Шаг цены или <see langword="null" />, если поле отсутствует.</summary>
    public decimal? TickSize { get; }

    /// <summary>Цена последней сделки или <see langword="null" />, если поле отсутствует.</summary>
    public decimal? LastTradePrice { get; }
}
