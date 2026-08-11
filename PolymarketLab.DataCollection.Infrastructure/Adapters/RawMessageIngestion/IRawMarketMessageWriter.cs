using PolymarketLab.DataCollection.Core.Ports.Dtos;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;

internal interface IRawMarketMessageWriter
{
    Task WriteBatchAsync(
        IReadOnlyCollection<RawMarketMessage> messages,
        IReadOnlyCollection<CollectorSessionProgressCheckpoint> checkpoints,
        CancellationToken cancellationToken);
}
