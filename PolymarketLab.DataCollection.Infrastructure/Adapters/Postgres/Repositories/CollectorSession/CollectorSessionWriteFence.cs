using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using System.Data.Common;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.CollectorSession;

internal static class CollectorSessionWriteFence
{
    public static async Task<IReadOnlySet<CollectorSessionId>> LockAsync(
        DataCollectionDbContext dbContext,
        IDbContextTransaction transaction,
        IEnumerable<CollectorSessionId> sessionIds,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction != transaction)
            throw new InvalidOperationException("The write fence requires the current database transaction.");

        var requestedIds = sessionIds
            .Distinct()
            .OrderBy(id => id.Value)
            .ToArray();
        if (requestedIds.Length == 0)
            return new HashSet<CollectorSessionId>();

        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            """
            SELECT id, invalidating_at IS NOT NULL
            FROM data_collection.collector_sessions
            WHERE id = ANY(@session_ids::uuid[])
            ORDER BY id
            FOR SHARE
            """;
        AddParameter(
            command,
            "session_ids",
            requestedIds.Select(id => id.Value).ToArray());
        var fenced = new HashSet<CollectorSessionId>();
        var found = 0;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                found++;
                if (reader.GetBoolean(1))
                    fenced.Add(CollectorSessionId.Create(reader.GetGuid(0)).Value);
            }
        }

        if (found != requestedIds.Length)
            throw new InvalidOperationException("A collector session was not found while acquiring a write fence.");

        return fenced;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
