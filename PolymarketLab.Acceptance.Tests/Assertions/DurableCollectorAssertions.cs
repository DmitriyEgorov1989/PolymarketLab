using Npgsql;

namespace PolymarketLab.Acceptance.Tests.Assertions;

internal sealed record DurableCollectorEvidence(
    long MessagesReceived,
    long MessagesEnqueued,
    long MessagesPersisted,
    long RawCount,
    long ProcessedCount);

internal static class DurableCollectorAssertions
{
    public static async Task<DurableCollectorEvidence> ReadAsync(
        string connectionString,
        Guid sessionId,
        int projectionVersion)
    {
        const string sql = """
            SELECT
                progress.messages_received,
                progress.messages_enqueued,
                progress.messages_persisted,
                (SELECT COUNT(*)
                   FROM data_collection.raw_market_messages AS raw
                  WHERE raw.session_id = progress.session_id) AS raw_count,
                (SELECT COUNT(*)
                   FROM data_collection.raw_message_normalizations AS normalization
                   JOIN data_collection.raw_market_messages AS raw
                     ON raw.id = normalization.raw_message_id
                  WHERE raw.session_id = progress.session_id
                    AND normalization.projection_version = @projection_version
                    AND normalization.status = 3) AS processed_count
              FROM data_collection.collector_session_progress AS progress
             WHERE progress.session_id = @session_id;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("projection_version", projectionVersion);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException($"Durable progress for session {sessionId} was not found.");

        return new DurableCollectorEvidence(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4));
    }
}
