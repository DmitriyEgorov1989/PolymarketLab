using Microsoft.EntityFrameworkCore;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.CollectorSession;

internal sealed class CollectorTokenReadinessRepository(DataCollectionDbContext dbContext)
    : ICollectorTokenReadinessRepository
{
    public async Task RecordInitialBookEnqueuedAsync(
        CollectorTokenReadiness readiness,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO data_collection.collector_token_readiness (
                session_id,
                connection_epoch,
                token_id,
                initial_book_enqueued_at)
            VALUES (
                {readiness.SessionId.Value},
                {readiness.ConnectionEpoch},
                {readiness.TokenId.Value},
                {readiness.InitialBookEnqueuedAt})
            ON CONFLICT DO NOTHING;
            """,
            cancellationToken);
    }

    public async Task<IReadOnlyCollection<CollectorTokenReadiness>> GetAsync(
        CollectorSessionId sessionId,
        long connectionEpoch,
        CancellationToken cancellationToken)
    {
        return await dbContext.CollectorTokenReadiness
            .AsNoTracking()
            .Where(readiness => readiness.SessionId == sessionId
                                && readiness.ConnectionEpoch == connectionEpoch)
            .Select(readiness => readiness.ToReadiness())
            .ToArrayAsync(cancellationToken);
    }
}
