using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;
using RawMessage = PolymarketLab.DataCollection.Core.Ports.Dtos.RawMarketMessage;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.RawMarketMessage;

internal sealed class RawMarketMessageWriter(DataCollectionDbContext dbContext)
    : IRawMarketMessageWriter
{
    public async Task WriteBatchAsync(
        IReadOnlyCollection<RawMessage> messages,
        CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
            return;

        var records = messages.Select(message => new RawMarketMessageRecord(
            message.SessionId,
            message.ReceivedAt,
            message.Payload.ToArray()));

        dbContext.RawMarketMessages.AddRange(records);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
