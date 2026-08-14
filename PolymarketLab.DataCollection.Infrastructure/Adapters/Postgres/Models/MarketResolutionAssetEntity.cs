using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

internal sealed class MarketResolutionAssetEntity
{
    private MarketResolutionAssetEntity()
    {
    }

    public MarketResolutionAssetEntity(long eventId, MarketResolvedAssetRecord record)
    {
        EventId = eventId;
        ItemIndex = record.ItemIndex;
        AssetId = record.AssetId;
    }

    public long Id { get; private set; }
    public long EventId { get; private set; }
    public int ItemIndex { get; private set; }
    public string AssetId { get; private set; } = string.Empty;
}
