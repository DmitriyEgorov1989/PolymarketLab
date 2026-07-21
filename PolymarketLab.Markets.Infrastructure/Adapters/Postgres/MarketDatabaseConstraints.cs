namespace PolymarketLab.Markets.Infrastructure.Adapters.Postgres;

internal static class MarketDatabaseConstraints
{
    public const string Slug = "ux_markets_slug";
    public const string ExternalId = "ux_markets_external_id";
    public const string ConditionId = "ux_markets_condition_id";

    public static bool IsIdentityConstraint(string? constraintName)
    {
        return constraintName is Slug or ExternalId or ConditionId;
    }
}
