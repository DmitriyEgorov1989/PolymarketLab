namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;

/// <summary>Полный снимок стакана, полученный из внешнего источника.</summary>
public sealed record OrderBookSnapshot
{
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

    public string MarketConditionId { get; }
    public string AssetId { get; }
    public long SourceTimestamp { get; }
    public string Hash { get; }
    public IReadOnlyList<OrderBookSnapshotLevel> Bids { get; }
    public IReadOnlyList<OrderBookSnapshotLevel> Asks { get; }
    public decimal MinimumOrderSize { get; }
    public decimal TickSize { get; }
    public bool NegativeRisk { get; }
    public decimal LastTradePrice { get; }
}

/// <summary>Агрегированный ценовой уровень внешнего снимка стакана.</summary>
public readonly record struct OrderBookSnapshotLevel
{
    public OrderBookSnapshotLevel(decimal price, decimal size)
    {
        if (price is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(price));
        if (size < 0)
            throw new ArgumentOutOfRangeException(nameof(size));

        Price = price;
        Size = size;
    }

    public decimal Price { get; }
    public decimal Size { get; }
}
