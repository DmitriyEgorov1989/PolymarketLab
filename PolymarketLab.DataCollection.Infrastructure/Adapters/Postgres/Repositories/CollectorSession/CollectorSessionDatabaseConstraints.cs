namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.CollectorSession;

internal static class CollectorSessionDatabaseConstraints
{
    public const string ActiveMarket = "ux_collector_sessions_active_market";
    public const string ActiveStatusFilter = "\"status\" IN (0, 1, 2)";

    public static bool IsActiveMarketConstraint(string? constraintName)
    {
        return constraintName == ActiveMarket;
    }
}
