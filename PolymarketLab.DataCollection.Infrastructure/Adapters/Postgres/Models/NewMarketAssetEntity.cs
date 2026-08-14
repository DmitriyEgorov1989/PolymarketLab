using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

internal sealed class NewMarketAssetEntity
{
    private NewMarketAssetEntity()
    {
    }

    public NewMarketAssetEntity(long eventId, NewMarketAssetRecord record)
    {
        EventId = eventId;
        ItemIndex = record.ItemIndex;
        AssetId = record.AssetId;
        Outcome = record.Outcome;
    }

    public long Id { get; private set; }
    public long EventId { get; private set; }
    public int ItemIndex { get; private set; }
    public string AssetId { get; private set; } = string.Empty;
    public string Outcome { get; private set; } = string.Empty;
}
