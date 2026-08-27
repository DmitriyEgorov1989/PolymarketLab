namespace PolymarketLab.Markets.Infrastructure.Adapters.Postgres;

internal static class MarketDatabaseConstraints
{
    public const string ExternalEventId = "ux_markets_external_event_id";
    public const string EventSlug = "ux_markets_event_slug";
    public const string MarketSlug = "ux_markets_market_slug";
    public const string ExternalMarketId = "ux_markets_external_market_id";
    public const string ConditionId = "ux_markets_condition_id";
    public const string ExternalTokenId = "ux_market_tokens_external_token_id";

    public static bool IsIdentityConstraint(string? constraintName)
    {
        return constraintName is ExternalEventId
            or EventSlug
            or MarketSlug
            or ExternalMarketId
            or ConditionId
            or ExternalTokenId;
    }
}
