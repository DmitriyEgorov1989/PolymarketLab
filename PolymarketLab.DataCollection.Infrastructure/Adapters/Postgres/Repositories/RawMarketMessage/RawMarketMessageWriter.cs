using Microsoft.EntityFrameworkCore;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;
using PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;
using RawMessage = PolymarketLab.DataCollection.Core.Ports.Dtos.RawMarketMessage;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.RawMarketMessage;

internal sealed class RawMarketMessageWriter(DataCollectionDbContext dbContext)
    : IRawMarketMessageWriter
{
    public async Task WriteBatchAsync(
        IReadOnlyCollection<RawMessage> messages,
        IReadOnlyCollection<CollectorSessionProgressCheckpoint> checkpoints,
        CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
            return;

        var records = messages.Select(message => new RawMarketMessageRecord(
            message.SessionId,
            message.ReceivedAt,
            message.Payload.ToArray())).ToArray();

        dbContext.RawMarketMessages.AddRange(records);

        var checkpointBySession = checkpoints.ToDictionary(
            checkpoint => checkpoint.SessionId);
        foreach (var group in messages.GroupBy(message => message.SessionId))
        {
            var progress = await dbContext.CollectorSessionProgress
                .SingleOrDefaultAsync(
                    current => current.SessionId == group.Key,
                    cancellationToken);
            if (progress is null)
            {
                progress = new CollectorSessionProgressRecord(group.Key);
                dbContext.CollectorSessionProgress.Add(progress);
            }

            var lastPersistedAt = group.Max(message => message.ReceivedAt);
            var checkpoint = checkpointBySession.GetValueOrDefault(group.Key)
                ?? new CollectorSessionProgressCheckpoint(
                    group.Key,
                    group.LongCount(),
                    lastPersistedAt,
                    0);
            progress.ApplyBatch(
                checkpoint,
                group.LongCount(),
                lastPersistedAt);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
