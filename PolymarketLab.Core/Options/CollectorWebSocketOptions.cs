namespace PolymarketLab.Core.Options;

public sealed class CollectorWebSocketOptions
{
    public const string SectionName = "CollectorWebSocket";
    public static readonly TimeSpan MaximumConnectTimeout =
        TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    public string Endpoint { get; init; } =
        "wss://ws-subscriptions-clob.polymarket.com/ws/market";

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public bool CustomFeatureEnabled { get; init; } = true;
}
