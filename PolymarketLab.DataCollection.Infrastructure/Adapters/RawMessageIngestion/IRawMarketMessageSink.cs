using PolymarketLab.DataCollection.Core.Ports.Dtos;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;

internal interface IRawMarketMessageSink
{
    ValueTask EnqueueAsync(
        RawMarketMessage message,
        CancellationToken cancellationToken);
}
