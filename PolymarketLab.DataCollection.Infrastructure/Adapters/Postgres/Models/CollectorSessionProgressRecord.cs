using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

internal sealed class CollectorSessionProgressRecord
{
    private CollectorSessionProgressRecord()
    {
    }

    public CollectorSessionProgressRecord(CollectorSessionId sessionId)
    {
        SessionId = sessionId;
    }

    public CollectorSessionId SessionId { get; private set; } = null!;
    public long MessagesReceived { get; private set; }
    public long MessagesPersisted { get; private set; }
    public DateTimeOffset? LastMessageAt { get; private set; }
    public long ReconnectCount { get; private set; }

    public void ApplyBatch(
        CollectorSessionProgressCheckpoint checkpoint,
        long persistedCount,
        DateTimeOffset lastPersistedAt)
    {
        MessagesPersisted += persistedCount;
        MessagesReceived = Math.Max(
            Math.Max(MessagesReceived, checkpoint.MessagesReceived),
            MessagesPersisted);
        LastMessageAt = Max(LastMessageAt, checkpoint.LastMessageAt, lastPersistedAt);
        ReconnectCount = Math.Max(ReconnectCount, checkpoint.ReconnectCount);
    }

    public void Checkpoint(CollectorSessionProgressCheckpoint checkpoint)
    {
        MessagesReceived = Math.Max(
            Math.Max(MessagesReceived, checkpoint.MessagesReceived),
            MessagesPersisted);
        LastMessageAt = Max(LastMessageAt, checkpoint.LastMessageAt);
        ReconnectCount = Math.Max(ReconnectCount, checkpoint.ReconnectCount);
    }

    public CollectorSessionProgress ToProgress() => new(
        SessionId,
        MessagesReceived,
        MessagesPersisted,
        LastMessageAt,
        ReconnectCount);

    private static DateTimeOffset? Max(params DateTimeOffset?[] values)
    {
        return values.Where(value => value.HasValue)
            .Max();
    }
}
