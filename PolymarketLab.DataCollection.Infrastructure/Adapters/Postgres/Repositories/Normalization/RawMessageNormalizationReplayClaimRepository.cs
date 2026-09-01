using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Normalization;

internal sealed class RawMessageNormalizationReplayClaimRepository(
    DataCollectionDbContext dbContext) : IRawMessageNormalizationReplayClaimRepository
{
    private const string EligibilitySql =
        """
        FROM data_collection.raw_market_messages AS raw
        INNER JOIN data_collection.collector_sessions AS session
          ON session.id = raw.session_id
        INNER JOIN data_collection.raw_message_normalizations AS source
          ON source.raw_message_id = raw.id
         AND source.projection_version = @source_projection_version
        LEFT JOIN data_collection.raw_message_normalizations AS target
          ON target.raw_message_id = raw.id
         AND target.projection_version = @target_projection_version
        WHERE session.invalidating_at IS NULL
          AND raw.id <= @high_watermark_raw_message_id
          AND source.completed_at <= @source_completed_before
          AND source.status IN (
              @processed_status,
              @unsupported_status,
              @invalid_status,
              @failed_status)
          AND (@session_id::uuid IS NULL OR raw.session_id = @session_id)
          AND (
              @event_type::text IS NULL
              OR EXISTS (
                  SELECT 1
                  FROM data_collection.normalized_events AS source_event
                  WHERE source_event.raw_message_id = raw.id
                    AND source_event.projection_version = @source_projection_version
                    AND source_event.event_type = @event_type
              )
          )
        """;

    private const string ClaimSql =
        """
        WITH writable_sessions AS MATERIALIZED (
            SELECT session.id
            FROM data_collection.collector_sessions AS session
            WHERE session.invalidating_at IS NULL
            ORDER BY session.id
            FOR SHARE
        ),
        candidates AS MATERIALIZED (
            SELECT raw.id
            FROM data_collection.raw_market_messages AS raw
            INNER JOIN writable_sessions AS session
              ON session.id = raw.session_id
            INNER JOIN data_collection.raw_message_normalizations AS source
              ON source.raw_message_id = raw.id
             AND source.projection_version = @source_projection_version
            LEFT JOIN data_collection.raw_message_normalizations AS target
              ON target.raw_message_id = raw.id
             AND target.projection_version = @target_projection_version
            WHERE raw.id <= @high_watermark_raw_message_id
              AND source.completed_at <= @source_completed_before
              AND source.status IN (
                  @processed_status,
                  @unsupported_status,
                  @invalid_status,
                  @failed_status)
              AND (@session_id::uuid IS NULL OR raw.session_id = @session_id)
              AND (
                  @event_type::text IS NULL
                  OR EXISTS (
                      SELECT 1
                      FROM data_collection.normalized_events AS source_event
                      WHERE source_event.raw_message_id = raw.id
                        AND source_event.projection_version = @source_projection_version
                        AND source_event.event_type = @event_type
                  )
              )
              AND (
                  target.raw_message_id IS NULL
                  OR target.status = @pending_status
                  OR (
                      target.status = @processing_status
                      AND (
                          target.claimed_at IS NULL
                          OR target.claimed_at < CURRENT_TIMESTAMP - @claim_timeout
                      )
                  )
              )
            ORDER BY raw.id
            LIMIT @batch_size
            FOR UPDATE OF raw SKIP LOCKED
        ),
        claimed AS (
            INSERT INTO data_collection.raw_message_normalizations AS target
                (raw_message_id, projection_version, status, attempt_count, claimed_at)
            SELECT
                candidates.id,
                @target_projection_version,
                @processing_status,
                1,
                CURRENT_TIMESTAMP
            FROM candidates
            ON CONFLICT (raw_message_id, projection_version)
            DO UPDATE SET
                status = @processing_status,
                attempt_count = target.attempt_count + 1,
                claimed_at = CURRENT_TIMESTAMP,
                completed_at = NULL,
                error_code = NULL,
                error_message = NULL,
                error_field = NULL
            WHERE target.status = @pending_status
               OR (
                   target.status = @processing_status
                   AND (
                       target.claimed_at IS NULL
                       OR target.claimed_at < CURRENT_TIMESTAMP - @claim_timeout
                   )
               )
            RETURNING raw_message_id, attempt_count
        )
        SELECT
            raw.id,
            raw.session_id,
            raw.received_at,
            raw.payload,
            claimed.attempt_count
        FROM claimed
        INNER JOIN data_collection.raw_market_messages AS raw
            ON raw.id = claimed.raw_message_id
        ORDER BY raw.id
        """;

    public async Task<NormalizationReplaySnapshot> CaptureSnapshotAsync(
        CancellationToken cancellationToken)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            """
            SELECT COALESCE(MAX(id), 0)::bigint, CURRENT_TIMESTAMP
            FROM data_collection.raw_market_messages
            """;
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            return new NormalizationReplaySnapshot(
                reader.GetInt64(0),
                reader.GetFieldValue<DateTimeOffset>(1));
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    public async Task<IReadOnlyList<ClaimedRawMessage>> ClaimBatchAsync(
        NormalizationReplayFilter filter,
        NormalizationReplaySnapshot snapshot,
        int batchSize,
        TimeSpan claimTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(filter.SourceProjectionVersion);
        if (filter.TargetProjectionVersion <= filter.SourceProjectionVersion)
            throw new ArgumentOutOfRangeException(nameof(filter));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        if (claimTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(claimTimeout));

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = ClaimSql;
        AddParameter(command, "source_projection_version", filter.SourceProjectionVersion);
        AddParameter(command, "target_projection_version", filter.TargetProjectionVersion);
        AddParameter(command, "high_watermark_raw_message_id", snapshot.HighWatermarkRawMessageId);
        AddParameter(command, "source_completed_before", snapshot.SourceCompletedBefore);
        AddParameter(command, "session_id", filter.SessionId?.Value ?? (object)DBNull.Value);
        AddParameter(command, "event_type", filter.EventType ?? (object)DBNull.Value);
        AddParameter(command, "batch_size", batchSize);
        AddParameter(command, "claim_timeout", claimTimeout);
        AddParameter(command, "pending_status", (int)NormalizationStatus.Pending);
        AddParameter(command, "processing_status", (int)NormalizationStatus.Processing);
        AddParameter(command, "processed_status", (int)NormalizationStatus.Processed);
        AddParameter(command, "unsupported_status", (int)NormalizationStatus.Unsupported);
        AddParameter(command, "invalid_status", (int)NormalizationStatus.Invalid);
        AddParameter(command, "failed_status", (int)NormalizationStatus.Failed);

        var claims = new List<ClaimedRawMessage>(batchSize);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var message = new RawMessageEnvelope(
                    reader.GetInt64(0),
                    CollectorSessionId.Create(reader.GetGuid(1)).Value,
                    reader.GetFieldValue<DateTimeOffset>(2),
                    reader.GetFieldValue<byte[]>(3));
                claims.Add(new ClaimedRawMessage(
                    message,
                    filter.TargetProjectionVersion,
                    reader.GetInt32(4)));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return claims;
    }

    public async Task<bool> HasRemainingAsync(
        NormalizationReplayFilter filter,
        NormalizationReplaySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            $"""
            SELECT EXISTS (
                SELECT 1
                {EligibilitySql}
                  AND (
                      target.raw_message_id IS NULL
                      OR target.status IN (@pending_status, @processing_status)
                  )
            )
            """;
        AddFilterParameters(command, filter, snapshot);
        AddParameter(command, "pending_status", (int)NormalizationStatus.Pending);
        AddParameter(command, "processing_status", (int)NormalizationStatus.Processing);
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static void AddFilterParameters(
        DbCommand command,
        NormalizationReplayFilter filter,
        NormalizationReplaySnapshot snapshot)
    {
        AddParameter(command, "source_projection_version", filter.SourceProjectionVersion);
        AddParameter(command, "target_projection_version", filter.TargetProjectionVersion);
        AddParameter(command, "high_watermark_raw_message_id", snapshot.HighWatermarkRawMessageId);
        AddParameter(command, "source_completed_before", snapshot.SourceCompletedBefore);
        AddParameter(command, "session_id", filter.SessionId?.Value ?? (object)DBNull.Value);
        AddParameter(command, "event_type", filter.EventType ?? (object)DBNull.Value);
        AddParameter(command, "processed_status", (int)NormalizationStatus.Processed);
        AddParameter(command, "unsupported_status", (int)NormalizationStatus.Unsupported);
        AddParameter(command, "invalid_status", (int)NormalizationStatus.Invalid);
        AddParameter(command, "failed_status", (int)NormalizationStatus.Failed);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
