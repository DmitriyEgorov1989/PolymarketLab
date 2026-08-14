using System.Data;
using Microsoft.EntityFrameworkCore;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Normalization;

internal interface INormalizationBacklogReader
{
    Task<NormalizationBacklogSnapshot> ReadAsync(
        int projectionVersion,
        TimeSpan claimTimeout,
        CancellationToken cancellationToken);
}

internal sealed class NormalizationBacklogReader(DataCollectionDbContext dbContext)
    : INormalizationBacklogReader
{
    public async Task<NormalizationBacklogSnapshot> ReadAsync(
        int projectionVersion,
        TimeSpan claimTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(projectionVersion);
        if (claimTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(claimTimeout));

        var connection = dbContext.Database.GetDbConnection();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    COUNT(*) FILTER (
                        WHERE normalization.raw_message_id IS NULL
                           OR normalization.status = @pending_status
                           OR (
                               normalization.status = @processing_status
                               AND (
                                   normalization.claimed_at IS NULL
                                   OR normalization.claimed_at
                                      < CURRENT_TIMESTAMP - @claim_timeout
                               )
                           )
                    )::bigint AS pending_messages,
                    COUNT(*) FILTER (
                        WHERE normalization.raw_message_id IS NULL
                           OR normalization.status IN (@pending_status, @processing_status)
                    )::bigint AS lag_messages
                FROM data_collection.raw_market_messages AS raw
                LEFT JOIN data_collection.raw_message_normalizations AS normalization
                  ON normalization.raw_message_id = raw.id
                 AND normalization.projection_version = @projection_version
                """;
            AddParameter(command, "projection_version", projectionVersion);
            AddParameter(command, "pending_status", (int)NormalizationStatus.Pending);
            AddParameter(command, "processing_status", (int)NormalizationStatus.Processing);
            AddParameter(command, "claim_timeout", claimTimeout);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            return new NormalizationBacklogSnapshot(
                projectionVersion,
                reader.GetInt64(0),
                reader.GetInt64(1));
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
