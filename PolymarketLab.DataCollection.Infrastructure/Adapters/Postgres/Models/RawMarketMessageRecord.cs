using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

internal sealed class RawMarketMessageRecord
{
    private RawMarketMessageRecord()
    {
    }

    public RawMarketMessageRecord(
        CollectorSessionId sessionId,
        long connectionEpoch,
        DateTimeOffset receivedAt,
        byte[] payload)
    {
        SessionId = sessionId;
        ConnectionEpoch = connectionEpoch;
        ReceivedAt = receivedAt;
        Payload = payload;
    }

    public long Id { get; private set; }
    public CollectorSessionId SessionId { get; private set; } = null!;

    /// <summary>Эпоха подключения, в которой принято полное text message.</summary>
    public long ConnectionEpoch { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public byte[] Payload { get; private set; } = [];
}
