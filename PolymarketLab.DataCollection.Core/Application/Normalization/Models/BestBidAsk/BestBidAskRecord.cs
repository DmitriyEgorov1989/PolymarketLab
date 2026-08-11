namespace PolymarketLab.DataCollection.Core.Application.Normalization.Models;

/// <summary>Лучшие цены покупки и продажи актива.</summary>
public sealed record BestBidAskRecord : NormalizedRecord
{
    /// <summary>Создаёт запись лучших цен актива.</summary>
    /// <param name="bestBid">Лучшая цена покупки от нуля до единицы.</param>
    /// <param name="bestAsk">Лучшая цена продажи от нуля до единицы.</param>
    /// <param name="spread">Неотрицательный спред из исходного события.</param>
    public BestBidAskRecord(decimal bestBid, decimal bestAsk, decimal spread)
    {
        if (bestBid is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(bestBid));
        if (bestAsk is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(bestAsk));
        if (spread < 0)
            throw new ArgumentOutOfRangeException(nameof(spread));

        BestBid = bestBid;
        BestAsk = bestAsk;
        Spread = spread;
    }

    /// <summary>Лучшая цена покупки.</summary>
    public decimal BestBid { get; }

    /// <summary>Лучшая цена продажи.</summary>
    public decimal BestAsk { get; }

    /// <summary>Спред, полученный от внешнего источника без пересчёта.</summary>
    public decimal Spread { get; }
}
