using Npgsql;

namespace PolymarketLab.Acceptance.Tests.Assertions;

internal sealed record DurableCollectorEvidence(
    long MessagesReceived,
    long MessagesEnqueued,
    long MessagesPersisted,
    long RawCount,
    long ProcessedCount,
    long UnexpectedNormalizationCount,
    long MismatchedNormalizedEventCount,
    long TerminalResolutionSourceCount,
    long ConsensusReferenceCount);

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
                     AND normalization.status = 3) AS processed_count,
                (SELECT COUNT(*)
                   FROM data_collection.raw_message_normalizations AS normalization
                   JOIN data_collection.raw_market_messages AS raw
                     ON raw.id = normalization.raw_message_id
                  WHERE raw.session_id = progress.session_id
                    AND (normalization.projection_version <> @projection_version
                         OR normalization.status <> 3)) AS unexpected_normalization_count,
                (SELECT COUNT(*)
                   FROM data_collection.normalized_events AS normalized
                   JOIN data_collection.raw_market_messages AS raw
                     ON raw.id = normalized.raw_message_id
                  WHERE raw.session_id = progress.session_id
                    AND (normalized.session_id <> progress.session_id
                         OR normalized.projection_version <> @projection_version)) AS mismatched_normalized_event_count,
                (SELECT COUNT(DISTINCT observation.source)
                   FROM data_collection.resolution_observations AS observation
                  WHERE observation.session_id = progress.session_id
                    AND observation.status = 2
                    AND observation.winner_token_id IS NOT NULL
                    AND ((observation.source = 0
                          AND observation.raw_message_id IS NOT NULL
                          AND EXISTS (
                              SELECT 1
                                FROM data_collection.raw_market_messages AS resolution_raw
                               WHERE resolution_raw.id = observation.raw_message_id
                                 AND resolution_raw.session_id = progress.session_id))
                         OR (observation.source IN (1, 2)
                             AND observation.raw_message_id IS NULL))) AS terminal_resolution_source_count,
                (SELECT COUNT(*)
                   FROM data_collection.resolution_states AS state
                   JOIN data_collection.resolution_observations AS primary_observation
                     ON primary_observation.id = state.primary_observation_id
                   JOIN data_collection.resolution_observations AS confirming_observation
                     ON confirming_observation.id = state.confirming_observation_id
                  WHERE state.session_id = progress.session_id
                    AND state.confirmed_at IS NOT NULL
                    AND primary_observation.session_id = progress.session_id
                    AND primary_observation.source = 1
                    AND primary_observation.status = 2
                    AND confirming_observation.session_id = progress.session_id
                    AND confirming_observation.source = 2
                    AND confirming_observation.status = 2) AS consensus_reference_count
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
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetInt64(7),
            reader.GetInt64(8));
    }
}
