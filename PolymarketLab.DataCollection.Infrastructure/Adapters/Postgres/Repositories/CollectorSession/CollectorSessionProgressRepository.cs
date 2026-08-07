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
            .SingleOrDefaultAsync(
                current => current.SessionId == sessionId,
                cancellationToken);

        return progress?.ToProgress() ?? CollectorSessionProgress.Empty(sessionId);
    }

    public async Task CheckpointAsync(
        CollectorSessionProgressCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        var progress = await dbContext.CollectorSessionProgress
            .SingleOrDefaultAsync(
                current => current.SessionId == checkpoint.SessionId,
                cancellationToken);

        if (progress is null)
        {
            progress = new CollectorSessionProgressRecord(checkpoint.SessionId);
            dbContext.CollectorSessionProgress.Add(progress);
        }

        progress.Checkpoint(checkpoint);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
