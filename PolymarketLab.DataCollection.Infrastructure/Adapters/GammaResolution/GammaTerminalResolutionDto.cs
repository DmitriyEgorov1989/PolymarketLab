using System.Text.Json.Serialization;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.GammaResolution;

internal sealed record GammaTerminalResolutionEventDto(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("slug")] string? Slug,
    [property: JsonPropertyName("markets")] GammaTerminalResolutionMarketDto?[]? Markets);

internal sealed record GammaTerminalResolutionMarketDto(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("slug")] string? Slug,
    [property: JsonPropertyName("conditionId")] string? ConditionId,
    [property: JsonPropertyName("closed")] bool? Closed,
    [property: JsonPropertyName("acceptingOrders")] bool? AcceptingOrders,
    [property: JsonPropertyName("umaResolutionStatus")] string? UmaResolutionStatus,
    [property: JsonPropertyName("closedTime")] DateTimeOffset? ClosedTime,
    [property: JsonPropertyName("outcomes")] string? Outcomes,
    [property: JsonPropertyName("clobTokenIds")] string? ClobTokenIds,
    [property: JsonPropertyName("outcomePrices")] string? OutcomePrices);
