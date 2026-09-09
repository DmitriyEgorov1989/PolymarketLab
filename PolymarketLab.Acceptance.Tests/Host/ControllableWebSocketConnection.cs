using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime.WebSockets;

namespace PolymarketLab.Acceptance.Tests.Host;

internal sealed class ControllableWebSocketFactory(ControllableWebSocketConnection connection)
    : ICollectorWebSocketFactory
{
    public ControllableWebSocketConnection Connection { get; } = connection;
    public int CreateCount { get; private set; }

    public ICollectorWebSocketConnection Create()
    {
        CreateCount++;
        return Connection;
    }
}

internal sealed class ControllableWebSocketConnection : ICollectorWebSocketConnection
{
    private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();
    private readonly TaskCompletionSource _subscribed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _closeReleased =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool AutoPong { get; set; } = true;
    public bool HoldClose { get; set; }
    public IReadOnlyList<string> SentMessages
    {
        get
        {
            lock (_sentMessages)
                return _sentMessages.ToArray();
        }
    }

    private readonly List<string> _sentMessages = [];

    public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task SendTextAsync(
        ReadOnlyMemory<byte> message,
        CancellationToken cancellationToken)
    {
        var text = Encoding.UTF8.GetString(message.Span);
        lock (_sentMessages)
            _sentMessages.Add(text);

        if (text == "PING")
        {
            if (AutoPong)
                Emit("PONG");
        }
        else
        {
            _subscribed.TrySetResult();
        }

        return Task.CompletedTask;
    }

    public async ValueTask<CollectorWebSocketReceiveResult> ReceiveAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var payload = await _incoming.Reader.ReadAsync(cancellationToken);
        payload.CopyTo(buffer);
        return new CollectorWebSocketReceiveResult(
            payload.Length,
            WebSocketMessageType.Text,
            true);
    }

    public Task CloseAsync(CancellationToken cancellationToken)
    {
        return HoldClose
            ? _closeReleased.Task.WaitAsync(cancellationToken)
            : Task.CompletedTask;
    }

    public void Emit(string payload) =>
        _incoming.Writer.TryWrite(Encoding.UTF8.GetBytes(payload));

    public Task WaitForSubscriptionAsync(CancellationToken cancellationToken) =>
        _subscribed.Task.WaitAsync(cancellationToken);

    public void ReleaseClose() => _closeReleased.TrySetResult();

    public void Dispose() => _incoming.Writer.TryComplete();
}
