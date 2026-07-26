namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime.WebSockets;

internal sealed class ClientWebSocketFactory : ICollectorWebSocketFactory
{
    public ICollectorWebSocketConnection Create()
    {
        return new ClientWebSocketConnection();
    }
}
