using System.Data;
using Microsoft.EntityFrameworkCore;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Normalization;

/// <summary>
/// Одним согласованным PostgreSQL read получает session-scoped снимок пригодности
/// нормализации: raw/ledger cardinality, counts по статусам ledger указанной
/// snapshot-версии и strict WebSocket resolution provenance без raw payload.
/// </summary>
public sealed class NormalizationSuitabilityReader(DataCollectionDbContext dbContext)
    : INormalizationSuitabilityReader
{
    /// <inheritdoc />
    public async Task<NormalizationSuitability> ReadAsync(
        CollectorSessionId sessionId,
        int projectionVersion,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(projectionVersion);

        var connection = dbContext.Database.GetDbConnection();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                WITH target_session AS
                (
                    SELECT
                        id,
                        resolution_signaled_at,
                        resolution_connection_epoch,
                        winning_token_id,
                        winning_outcome
                    FROM data_collection.collector_sessions
                    WHERE id = @session_id
                ),
                session_raw AS
                (
                    SELECT raw.id
                    FROM data_collection.raw_market_messages AS raw
                    WHERE raw.session_id = @session_id
                ),
                snapshot_ledger AS
                (
                    SELECT normalization.raw_message_id, normalization.status
                    FROM data_collection.raw_message_normalizations AS normalization
                    INNER JOIN session_raw AS raw
                        ON raw.id = normalization.raw_message_id
                    WHERE normalization.projection_version = @projection_version
                ),
                counts AS
                (
                    SELECT
                        (SELECT COUNT(*)::bigint FROM session_raw) AS raw_count,
                        COUNT(*)::bigint AS ledger_count,
                        COUNT(*) FILTER (WHERE status = @processed_status)::bigint AS processed_count,
                        COUNT(*) FILTER (WHERE status = @pending_status)::bigint AS pending_count,
                        COUNT(*) FILTER (WHERE status = @processing_status)::bigint AS processing_count,
                        COUNT(*) FILTER (WHERE status = @unsupported_status)::bigint AS unsupported_count,
                        COUNT(*) FILTER (WHERE status = @invalid_status)::bigint AS invalid_count,
                        COUNT(*) FILTER (WHERE status = @failed_status)::bigint AS failed_count
                    FROM snapshot_ledger
                )
                SELECT
                    counts.raw_count,
                    counts.ledger_count,
                    counts.processed_count,
                    counts.pending_count,
                    counts.processing_count,
                    counts.unsupported_count,
                    counts.invalid_count,
                    counts.failed_count,
                    EXISTS
                    (
                        SELECT 1
                        FROM target_session AS session
                        INNER JOIN data_collection.resolution_observations AS observation
                            ON observation.session_id = session.id
                           AND observation.source = @websocket_source
                           AND observation.status = @terminal_observation_status
                           AND observation.observed_at = session.resolution_signaled_at
                           AND observation.connection_epoch = session.resolution_connection_epoch
                           AND observation.winner_token_id = session.winning_token_id
                           AND observation.winner_outcome = session.winning_outcome
                        INNER JOIN session_raw AS raw
                            ON raw.id = observation.raw_message_id
                        INNER JOIN snapshot_ledger AS ledger
                            ON ledger.raw_message_id = raw.id
                           AND ledger.status = @processed_status
                        INNER JOIN data_collection.normalized_events AS normalized
                            ON normalized.raw_message_id = observation.raw_message_id
                           AND normalized.raw_item_index = observation.raw_item_index
                           AND normalized.projection_version = @projection_version
                           AND normalized.event_type = 'market_resolved'
                    ) AS resolution_raw_item_processed
                FROM counts
                """;
            AddParameter(command, "session_id", sessionId.Value);
            AddParameter(command, "projection_version", projectionVersion);
            AddParameter(
                command,
                "websocket_source",
                (int)ResolutionObservationSource.WebSocket);
            AddParameter(
                command,
                "terminal_observation_status",
                (int)DurableResolutionObservationStatus.Terminal);
            AddParameter(command, "processed_status", (int)NormalizationStatus.Processed);
            AddParameter(command, "pending_status", (int)NormalizationStatus.Pending);
            AddParameter(command, "processing_status", (int)NormalizationStatus.Processing);
            AddParameter(command, "unsupported_status", (int)NormalizationStatus.Unsupported);
            AddParameter(command, "invalid_status", (int)NormalizationStatus.Invalid);
            AddParameter(command, "failed_status", (int)NormalizationStatus.Failed);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            return new NormalizationSuitability(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetInt64(7),
                reader.GetBoolean(8));
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static void AddParameter(IDbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
