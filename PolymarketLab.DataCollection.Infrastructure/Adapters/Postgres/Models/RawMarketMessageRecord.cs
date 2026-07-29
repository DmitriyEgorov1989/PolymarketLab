using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

internal sealed class RawMarketMessageRecord
{
    private RawMarketMessageRecord()
    {
    }

    public RawMarketMessageRecord(
        CollectorSessionId sessionId,
        DateTimeOffset receivedAt,
        byte[] payload)
    {
        SessionId = sessionId;
        ReceivedAt = receivedAt;
        Payload = payload;
    }

    public long Id { get; private set; }
    public CollectorSessionId SessionId { get; private set; } = null!;
    public DateTimeOffset ReceivedAt { get; private set; }
    public byte[] Payload { get; private set; } = [];
}
