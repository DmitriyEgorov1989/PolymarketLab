using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

internal sealed class PriceChangeItemEntity
{
    private PriceChangeItemEntity()
    {
    }

    public PriceChangeItemEntity(
        long eventId,
        long? sourceTimestamp,
        PriceChangeRecord record)
    {
        EventId = eventId;
        ItemIndex = record.ItemIndex;
        AssetId = record.AssetId;
        SourceTimestamp = sourceTimestamp;
        Price = record.Price;
        Size = record.Size;
        Side = record.Side;
        Hash = record.Hash;
        BestBid = record.BestBid;
        BestAsk = record.BestAsk;
    }

    public long Id { get; private set; }
    public long EventId { get; private set; }
    public int ItemIndex { get; private set; }
    public string AssetId { get; private set; } = string.Empty;
    public long? SourceTimestamp { get; private set; }
    public decimal Price { get; private set; }
    public decimal Size { get; private set; }
    public TradeSide Side { get; private set; }
    public string? Hash { get; private set; }
    public decimal? BestBid { get; private set; }
    public decimal? BestAsk { get; private set; }
}
