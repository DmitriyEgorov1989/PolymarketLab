using System.Net.WebSockets;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime.WebSockets;

internal sealed class ClientWebSocketConnection : ICollectorWebSocketConnection
{
    private readonly ClientWebSocket _socket = new();

    public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        return _socket.ConnectAsync(endpoint, cancellationToken);
    }

    public Task SendTextAsync(
        ReadOnlyMemory<byte> message,
        CancellationToken cancellationToken)
    {
        return _socket.SendAsync(
                message,
                WebSocketMessageType.Text,
                true,
                cancellationToken)
            .AsTask();
    }

    public async ValueTask<CollectorWebSocketReceiveResult> ReceiveAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var result = await _socket.ReceiveAsync(buffer, cancellationToken);
        return new CollectorWebSocketReceiveResult(
            result.Count,
            result.MessageType,
            result.EndOfMessage);
    }

    public Task CloseAsync(CancellationToken cancellationToken)
    {
        if (_socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
            return Task.CompletedTask;

        return _socket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "Collector stopped.",
            cancellationToken);
    }

    public void Dispose()
    {
        _socket.Dispose();
    }
}
