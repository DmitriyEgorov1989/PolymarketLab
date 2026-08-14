using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Normalization;

internal sealed class RawMessageNormalizationClaimRepository(DataCollectionDbContext dbContext)
    : IRawMessageNormalizationClaimRepository
{
    private const string ClaimSql =
        """
        WITH candidates AS MATERIALIZED (
            SELECT raw.id
            FROM data_collection.raw_market_messages AS raw
            LEFT JOIN data_collection.raw_message_normalizations AS normalization
              ON normalization.raw_message_id = raw.id
             AND normalization.projection_version = @projection_version
            WHERE normalization.raw_message_id IS NULL
               OR normalization.status = @pending_status
               OR (
                    normalization.status = @processing_status
                    AND (
                        normalization.claimed_at IS NULL
                        OR normalization.claimed_at < CURRENT_TIMESTAMP - @claim_timeout
                    )
               )
            ORDER BY raw.id
            LIMIT @batch_size
            FOR UPDATE OF raw SKIP LOCKED
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
                error_message = NULL
            WHERE normalization.status = @pending_status
               OR (
                    normalization.status = @processing_status
                    AND (
                        normalization.claimed_at IS NULL
                        OR normalization.claimed_at < CURRENT_TIMESTAMP - @claim_timeout
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
