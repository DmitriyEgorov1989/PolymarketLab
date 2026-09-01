using Microsoft.EntityFrameworkCore;
using PolymarketLab.DataCollection.Core.Ports.Dtos;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.CollectorSession;

internal static class CollectorSessionProgressUpsert
{
    public static Task ExecuteAsync(
        DataCollectionDbContext dbContext,
        CollectorSessionProgressCheckpoint checkpoint,
        long persistedIncrement,
        DateTimeOffset? lastPersistedAt,
        CancellationToken cancellationToken)
    {
        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO data_collection.collector_session_progress AS progress (
                session_id,
                current_connection_epoch,
                messages_received,
                messages_enqueued,
                messages_persisted,
                last_message_at,
                reconnect_count)
            VALUES (
                {checkpoint.SessionId.Value},
                {checkpoint.CurrentConnectionEpoch},
                {checkpoint.MessagesReceived},
                {checkpoint.MessagesEnqueued},
                GREATEST({checkpoint.MessagesPersisted}, {persistedIncrement}),
                GREATEST(
                    CAST({checkpoint.LastMessageAt} AS timestamp with time zone),
                    CAST({lastPersistedAt} AS timestamp with time zone)),
                {checkpoint.ReconnectCount})
            ON CONFLICT (session_id) DO UPDATE SET
                current_connection_epoch = GREATEST(
                    progress.current_connection_epoch,
                    {checkpoint.CurrentConnectionEpoch}),
                messages_received = GREATEST(
                    progress.messages_received,
                    {checkpoint.MessagesReceived}),
                messages_enqueued = GREATEST(
                    progress.messages_enqueued,
                    {checkpoint.MessagesEnqueued}),
                messages_persisted = GREATEST(
                    progress.messages_persisted + {persistedIncrement},
                    {checkpoint.MessagesPersisted}),
                last_message_at = GREATEST(
                    progress.last_message_at,
                    CAST({checkpoint.LastMessageAt} AS timestamp with time zone),
                    CAST({lastPersistedAt} AS timestamp with time zone)),
                reconnect_count = GREATEST(
                    progress.reconnect_count,
                    {checkpoint.ReconnectCount});
            """,
            cancellationToken);
    }
}
