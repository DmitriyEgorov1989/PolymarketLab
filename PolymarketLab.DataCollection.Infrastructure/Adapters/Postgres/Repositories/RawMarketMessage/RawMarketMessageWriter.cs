using Microsoft.EntityFrameworkCore;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.CollectorSession;
using PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;
using RawMessage = PolymarketLab.DataCollection.Core.Ports.Dtos.RawMarketMessage;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.RawMarketMessage;

internal sealed class RawMarketMessageWriter(DataCollectionDbContext dbContext)
    : IRawMarketMessageWriter
{
    public async Task<RawMarketMessageWriteResult> WriteBatchAsync(
        IReadOnlyCollection<RawMessage> messages,
        IReadOnlyCollection<CollectorSessionProgressCheckpoint> checkpoints,
        CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
            return RawMarketMessageWriteResult.Empty;

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);
        var fencedSessionIds = await CollectorSessionWriteFence.LockAsync(
            dbContext,
            transaction,
            messages.Select(message => message.SessionId),
            cancellationToken);
        var writableMessages = messages
            .Where(message => !fencedSessionIds.Contains(message.SessionId))
            .ToArray();
        var persistedSessionIds = writableMessages
            .Select(message => message.SessionId)
            .ToHashSet();
        if (writableMessages.Length == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new RawMarketMessageWriteResult(
                persistedSessionIds,
                fencedSessionIds);
        }

        var records = writableMessages.Select(message => new RawMarketMessageRecord(
            message.SessionId,
            message.ConnectionEpoch,
            message.ReceivedAt,
            message.Payload.ToArray())).ToArray();
        dbContext.RawMarketMessages.AddRange(records);
        await dbContext.SaveChangesAsync(cancellationToken);

        var checkpointBySession = checkpoints.ToDictionary(
            checkpoint => checkpoint.SessionId);
        foreach (var group in writableMessages.GroupBy(message => message.SessionId))
        {
            var lastPersistedAt = group.Max(message => message.ReceivedAt);
            var checkpoint = checkpointBySession.GetValueOrDefault(group.Key)
                ?? new CollectorSessionProgressCheckpoint(
                    group.Key,
                    group.Max(message => message.ConnectionEpoch),
                    group.LongCount(),
                    group.LongCount(),
                    0,
                    lastPersistedAt,
                    0);
            checkpoint = checkpoint with
            {
                CurrentConnectionEpoch = Math.Max(
                    checkpoint.CurrentConnectionEpoch,
                    group.Max(message => message.ConnectionEpoch))
            };
            await CollectorSessionProgressUpsert.ExecuteAsync(
                dbContext,
                checkpoint,
                group.LongCount(),
                lastPersistedAt,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new RawMarketMessageWriteResult(
            persistedSessionIds,
            fencedSessionIds);
    }
}
