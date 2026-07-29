using System.Net.WebSockets;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime.WebSockets;

internal readonly record struct CollectorWebSocketReceiveResult(
    int Count,
    WebSocketMessageType MessageType,
    bool EndOfMessage);
