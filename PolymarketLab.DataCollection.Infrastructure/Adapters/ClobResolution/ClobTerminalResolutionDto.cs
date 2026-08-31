using System.Text.Json.Serialization;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.ClobResolution;

internal sealed record ClobTerminalResolutionDto(
    [property: JsonPropertyName("condition_id")] string? ConditionId,
    [property: JsonPropertyName("closed")] bool? Closed,
    [property: JsonPropertyName("accepting_orders")] bool? AcceptingOrders,
    [property: JsonPropertyName("tokens")] ClobTerminalResolutionTokenDto?[]? Tokens);

internal sealed record ClobTerminalResolutionTokenDto(
    [property: JsonPropertyName("token_id")] string? TokenId,
    [property: JsonPropertyName("outcome")] string? Outcome,
    [property: JsonPropertyName("price")] decimal? Price,
    [property: JsonPropertyName("winner")] bool? Winner);
