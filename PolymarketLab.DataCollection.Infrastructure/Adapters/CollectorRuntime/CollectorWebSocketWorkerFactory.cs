using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime.WebSockets;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;

internal sealed class CollectorWebSocketWorkerFactory(
    ICollectorWebSocketFactory webSocketFactory,
    IOptions<CollectorWebSocketOptions> options,
    IRawMarketMessageSink messageSink,
    TimeProvider timeProvider,
    IHostApplicationLifetime applicationLifetime,
    ILogger<CollectorWebSocketWorker> logger)
    : ICollectorWorkerFactory
{
    public ICollectorWorker Create(CollectorRuntimeStartRequest request)
    {
        return new CollectorWebSocketWorker(
            request,
            webSocketFactory,
            options.Value,
            messageSink,
            timeProvider,
            applicationLifetime,
            logger);
    }
}
