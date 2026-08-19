using System.Text.Json.Serialization;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.OrderBookRest;

internal sealed record ExternalOrderBookSnapshotDto(
    [property: JsonPropertyName("market")] string? Market,
    [property: JsonPropertyName("asset_id")] string? AssetId,
    [property: JsonPropertyName("timestamp")] string? Timestamp,
    [property: JsonPropertyName("hash")] string? Hash,
    [property: JsonPropertyName("bids")] ExternalOrderBookLevelDto?[]? Bids,
    [property: JsonPropertyName("asks")] ExternalOrderBookLevelDto?[]? Asks,
    [property: JsonPropertyName("min_order_size")] string? MinimumOrderSize,
    [property: JsonPropertyName("tick_size")] string? TickSize,
    [property: JsonPropertyName("neg_risk")] bool? NegativeRisk,
    [property: JsonPropertyName("last_trade_price")] string? LastTradePrice);

internal sealed record ExternalOrderBookLevelDto(
    [property: JsonPropertyName("price")] string? Price,
    [property: JsonPropertyName("size")] string? Size);
