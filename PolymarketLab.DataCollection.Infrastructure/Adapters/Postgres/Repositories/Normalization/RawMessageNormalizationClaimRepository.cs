using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Normalization;

internal sealed class RawMessageNormalizationClaimRepository(DataCollectionDbContext dbContext)
    : IRawMessageNormalizationClaimRepository
{
    private const string ClaimSql =
        """
        WITH target_session AS MATERIALIZED (
            SELECT
                session.id,
                session.status,
                session.phase,
                session.awaiting_normalization_at,
                COALESCE(cursor.last_claimed_raw_message_id, 0) AS last_claimed_raw_message_id
            FROM data_collection.collector_sessions AS session
            LEFT JOIN data_collection.normalization_session_cursors AS cursor
              ON cursor.session_id = session.id
             AND cursor.projection_version = @projection_version
            WHERE session.invalidating_at IS NULL
              AND session.status IN (@starting_status, @running_status, @stopping_status)
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM data_collection.raw_market_messages AS active_raw
                  INNER JOIN data_collection.raw_message_normalizations AS active_normalization
                    ON active_normalization.raw_message_id = active_raw.id
                   AND active_normalization.projection_version = @projection_version
                  WHERE active_raw.session_id = session.id
                    AND active_normalization.status = @processing_status
                    AND active_normalization.claimed_at >= CURRENT_TIMESTAMP - @claim_timeout
              )
              AND
              (
                  EXISTS
                  (
                      SELECT 1
                      FROM data_collection.raw_message_normalizations AS retry_normalization
                      INNER JOIN data_collection.raw_market_messages AS retry_raw
                        ON retry_raw.id = retry_normalization.raw_message_id
                      WHERE retry_raw.session_id = session.id
                        AND retry_normalization.projection_version = @projection_version
                        AND
                        (
                            retry_normalization.status = @pending_status
                            OR
                            (
                                retry_normalization.status = @processing_status
                                AND
                                (
                                    retry_normalization.claimed_at IS NULL
                                    OR retry_normalization.claimed_at < CURRENT_TIMESTAMP - @claim_timeout
                                )
                            )
                        )
                  )
                  OR EXISTS
                  (
                      SELECT 1
                      FROM data_collection.raw_market_messages AS new_raw
                      WHERE new_raw.session_id = session.id
                        AND new_raw.id > COALESCE(cursor.last_claimed_raw_message_id, 0)
                  )
              )
            ORDER BY
                CASE
                    WHEN session.status = @stopping_status
                     AND session.phase = @awaiting_normalization_phase
                    THEN 0
                    ELSE 1
                END,
                CASE
                    WHEN session.status = @stopping_status
                     AND session.phase = @awaiting_normalization_phase
                    THEN session.awaiting_normalization_at
                END NULLS LAST,
                session.id
            LIMIT 1
            FOR UPDATE OF session SKIP LOCKED
        ),
        candidate_ids AS MATERIALIZED (
            SELECT candidate.id
            FROM target_session AS session
            CROSS JOIN LATERAL
            (
                SELECT eligible.id
                FROM
                (
                    SELECT retry_raw.id
                    FROM data_collection.raw_message_normalizations AS retry_normalization
                    INNER JOIN data_collection.raw_market_messages AS retry_raw
                      ON retry_raw.id = retry_normalization.raw_message_id
                    WHERE retry_raw.session_id = session.id
                      AND retry_normalization.projection_version = @projection_version
                      AND
                      (
                          retry_normalization.status = @pending_status
                          OR
                          (
                              retry_normalization.status = @processing_status
                              AND
                              (
                                  retry_normalization.claimed_at IS NULL
                                  OR retry_normalization.claimed_at < CURRENT_TIMESTAMP - @claim_timeout
                              )
                          )
                      )
                    UNION
                    SELECT new_raw.id
                    FROM data_collection.raw_market_messages AS new_raw
                    WHERE new_raw.session_id = session.id
                      AND new_raw.id > session.last_claimed_raw_message_id
                ) AS eligible
                ORDER BY eligible.id
                LIMIT @batch_size
            ) AS candidate
        ),
        candidates AS MATERIALIZED (
            SELECT raw.id
            FROM candidate_ids AS candidate
            INNER JOIN data_collection.raw_market_messages AS raw
              ON raw.id = candidate.id
            ORDER BY raw.id
            FOR UPDATE OF raw
        ),
        claimed AS (
            INSERT INTO data_collection.raw_message_normalizations AS normalization
                (raw_message_id, projection_version, status, attempt_count, claimed_at)
            SELECT
                candidates.id,
                @projection_version,
                @processing_status,
                1,
                CURRENT_TIMESTAMP
            FROM candidates
            ON CONFLICT (raw_message_id, projection_version)
            DO UPDATE SET
                status = @processing_status,
                attempt_count = normalization.attempt_count + 1,
                claimed_at = CURRENT_TIMESTAMP,
                completed_at = NULL,
                error_code = NULL,
                error_message = NULL,
                error_field = NULL
            WHERE normalization.status = @pending_status
               OR (
                    normalization.status = @processing_status
                    AND (
                        normalization.claimed_at IS NULL
                        OR normalization.claimed_at < CURRENT_TIMESTAMP - @claim_timeout
                    )
               )
            RETURNING raw_message_id, attempt_count
        ),
        cursor_advanced AS (
            INSERT INTO data_collection.normalization_session_cursors AS cursor
                (session_id, projection_version, last_claimed_raw_message_id)
            SELECT
                session.id,
                @projection_version,
                MAX(claimed.raw_message_id)
            FROM target_session AS session
            CROSS JOIN claimed
            GROUP BY session.id
            ON CONFLICT (session_id, projection_version)
            DO UPDATE SET
                last_claimed_raw_message_id = GREATEST(
                    cursor.last_claimed_raw_message_id,
                    EXCLUDED.last_claimed_raw_message_id)
            RETURNING session_id
        )
        SELECT
            raw.id,
            raw.session_id,
            raw.received_at,
            raw.payload,
            claimed.attempt_count
        FROM claimed
        CROSS JOIN cursor_advanced
        INNER JOIN data_collection.raw_market_messages AS raw
            ON raw.id = claimed.raw_message_id
        INNER JOIN data_collection.collector_sessions AS session
            ON session.id = raw.session_id
        ORDER BY
            CASE
                WHEN session.status = @stopping_status
                 AND session.phase = @awaiting_normalization_phase
                THEN 0
                ELSE 1
            END,
            CASE
                WHEN session.status = @stopping_status
                 AND session.phase = @awaiting_normalization_phase
                THEN session.awaiting_normalization_at
            END NULLS LAST,
            raw.id
        """;

    public async Task<IReadOnlyList<ClaimedRawMessage>> ClaimBatchAsync(
        int projectionVersion,
        int batchSize,
        TimeSpan claimTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(projectionVersion);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        if (claimTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(claimTimeout));

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = ClaimSql;
        AddParameter(command, "projection_version", projectionVersion);
        AddParameter(command, "batch_size", batchSize);
        AddParameter(command, "claim_timeout", claimTimeout);
        AddParameter(command, "pending_status", (int)NormalizationStatus.Pending);
        AddParameter(command, "processing_status", (int)NormalizationStatus.Processing);
        AddParameter(command, "stopping_status", (int)CollectorSessionStatus.Stopping);
        AddParameter(command, "starting_status", (int)CollectorSessionStatus.Starting);
        AddParameter(command, "running_status", (int)CollectorSessionStatus.Running);
        AddParameter(
            command,
            "awaiting_normalization_phase",
            (int)CollectorSessionPhase.AwaitingNormalization);

        var claimedMessages = new List<ClaimedRawMessage>(batchSize);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var sessionId = CollectorSessionId.Create(reader.GetGuid(1)).Value;
                var message = new RawMessageEnvelope(
                    reader.GetInt64(0),
                    sessionId,
                    reader.GetFieldValue<DateTimeOffset>(2),
                    reader.GetFieldValue<byte[]>(3));
                claimedMessages.Add(new ClaimedRawMessage(
                    message,
                    projectionVersion,
                    reader.GetInt32(4)));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return claimedMessages;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
