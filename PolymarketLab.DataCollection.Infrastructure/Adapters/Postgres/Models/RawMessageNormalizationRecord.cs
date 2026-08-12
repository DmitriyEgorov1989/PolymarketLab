using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

internal sealed class RawMessageNormalizationRecord
{
    private RawMessageNormalizationRecord()
    {
    }

    public long RawMessageId { get; private set; }
    public int ProjectionVersion { get; private set; }
    public NormalizationStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset? ClaimedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
}
