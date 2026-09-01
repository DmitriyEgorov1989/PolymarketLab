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

    /// <summary>Последняя сохранённая эпоха подключения; 0 означает отсутствие подключения.</summary>
    public long CurrentConnectionEpoch { get; private set; }
    public long MessagesReceived { get; private set; }

    /// <summary>Количество сообщений, успешно переданных в bounded ingestion.</summary>
    public long MessagesEnqueued { get; private set; }
    public long MessagesPersisted { get; private set; }
    public DateTimeOffset? LastMessageAt { get; private set; }
    public long ReconnectCount { get; private set; }

    public CollectorSessionProgress ToProgress(long rawMessageCount) => new(
        SessionId,
        CurrentConnectionEpoch,
        MessagesReceived,
        MessagesEnqueued,
        MessagesPersisted,
        rawMessageCount,
        LastMessageAt,
        ReconnectCount);
}
