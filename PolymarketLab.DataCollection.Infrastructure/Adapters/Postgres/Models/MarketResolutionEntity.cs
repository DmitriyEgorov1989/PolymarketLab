using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

internal sealed class MarketResolutionEntity
{
    private MarketResolutionEntity()
    {
    }

    public MarketResolutionEntity(long eventId, MarketResolvedRecord record)
    {
        EventId = eventId;
        ExternalMarketId = record.ExternalMarketId;
        WinningAssetId = record.WinningAssetId;
        WinningOutcome = record.WinningOutcome;
    }

    public long EventId { get; private set; }
    public string ExternalMarketId { get; private set; } = string.Empty;
    public string WinningAssetId { get; private set; } = string.Empty;
    public string WinningOutcome { get; private set; } = string.Empty;
}
