using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

internal sealed class NormalizedEventRecord
{
    private NormalizedEventRecord()
    {
    }

    public long Id { get; private set; }
    public long RawMessageId { get; private set; }
    public int RawItemIndex { get; private set; }
    public int ProjectionVersion { get; private set; }
    public int NormalizerVersion { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public CollectorSessionId SessionId { get; private set; } = null!;
    public DateTimeOffset ReceivedAt { get; private set; }
    public long? SourceTimestamp { get; private set; }
    public string? MarketConditionId { get; private set; }
    public string? AssetId { get; private set; }
    public DateTimeOffset NormalizedAt { get; private set; }
}
