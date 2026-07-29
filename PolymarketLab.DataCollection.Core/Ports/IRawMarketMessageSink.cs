using PolymarketLab.DataCollection.Core.Ports.Dtos;

namespace PolymarketLab.DataCollection.Core.Ports;

public interface IRawMarketMessageSink
{
    ValueTask EnqueueAsync(
        RawMarketMessage message,
        CancellationToken cancellationToken);
}
