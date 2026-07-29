using PolymarketLab.DataCollection.Core.Ports.Dtos;

namespace PolymarketLab.DataCollection.Core.Ports;

public interface IRawMarketMessageWriter
{
    Task WriteBatchAsync(
        IReadOnlyCollection<RawMarketMessage> messages,
        CancellationToken cancellationToken);
}
