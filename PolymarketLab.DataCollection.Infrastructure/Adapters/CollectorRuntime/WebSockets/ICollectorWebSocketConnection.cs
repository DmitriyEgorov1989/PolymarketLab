namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime.WebSockets;

internal interface ICollectorWebSocketConnection : IDisposable
{
    Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken);

    Task SendTextAsync(
        ReadOnlyMemory<byte> message,
        CancellationToken cancellationToken);

    Task CloseAsync(CancellationToken cancellationToken);
}
