namespace PolymarketLab.Core.Options;

public sealed class CollectorWebSocketOptions
{
    public const string SectionName = "CollectorWebSocket";
    public const int MaximumSupportedMessageSize = 16 * 1024 * 1024;
    public static readonly TimeSpan MaximumConnectTimeout =
        TimeSpan.FromMilliseconds(uint.MaxValue - 1);
    public static readonly TimeSpan MaximumStopTimeout =
        TimeSpan.FromMilliseconds(uint.MaxValue - 1);
    public static readonly TimeSpan MaximumHeartbeatInterval =
        TimeSpan.FromMilliseconds(uint.MaxValue - 1);
    public static readonly TimeSpan MaximumHeartbeatTimeout =
        TimeSpan.FromMilliseconds(uint.MaxValue - 1);
    public static readonly TimeSpan MaximumReconnectDelay =
        TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    public string Endpoint { get; init; } =
        "wss://ws-subscriptions-clob.polymarket.com/ws/market";

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan HeartbeatTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(1);

    public int ReceiveBufferSize { get; init; } = 16 * 1024;

    public int MaximumMessageSize { get; init; } = 1024 * 1024;

    public bool CustomFeatureEnabled { get; init; } = true;
}
