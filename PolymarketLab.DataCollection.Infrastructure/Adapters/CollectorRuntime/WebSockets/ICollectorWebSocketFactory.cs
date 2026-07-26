namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime.WebSockets;

internal interface ICollectorWebSocketFactory
{
    ICollectorWebSocketConnection Create();
}
