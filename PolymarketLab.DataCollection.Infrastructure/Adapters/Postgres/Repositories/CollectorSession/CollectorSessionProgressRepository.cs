using Microsoft.EntityFrameworkCore;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.CollectorSession;

internal sealed class CollectorSessionProgressRepository(DataCollectionDbContext dbContext)
    : ICollectorSessionProgressRepository
{
    public async Task<CollectorSessionProgress> GetAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken)
    {
        var progress = await dbContext.CollectorSessionProgress
            .AsNoTracking()
            .Where(current => current.SessionId == sessionId)
            .Select(current => new
            {
                Progress = current,
                RawMessageCount = dbContext.RawMarketMessages.LongCount(
                    message => message.SessionId == sessionId)
            })
            .SingleOrDefaultAsync(
                cancellationToken);

        return progress?.Progress.ToProgress(progress.RawMessageCount)
            ?? CollectorSessionProgress.Empty(sessionId);
    }

    public async Task CheckpointAsync(
        CollectorSessionProgressCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        await CollectorSessionProgressUpsert.ExecuteAsync(
            dbContext,
            checkpoint,
            0,
            null,
            cancellationToken);
    }
}
