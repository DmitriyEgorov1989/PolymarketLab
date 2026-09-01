using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PolymarketLab.SharedKernel.DomainModels.Ids;

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

        var fenced = new HashSet<CollectorSessionId>();
        foreach (var sessionId in sessionIds.Distinct().OrderBy(id => id.Value))
        {
            var state = await dbContext.Database.SqlQueryRaw<int>(
                    """
                    SELECT CASE WHEN invalidating_at IS NULL THEN 0 ELSE 1 END AS "Value"
                    FROM data_collection.collector_sessions
                    WHERE id = {0}
                    FOR SHARE
                    """,
                    sessionId.Value)
                .ToArrayAsync(cancellationToken);
            if (state.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Collector session '{sessionId.Value}' was not found while acquiring a write fence.");
            }

            if (state[0] == 1)
            {
                fenced.Add(sessionId);
            }
        }

        return fenced;
    }
}
