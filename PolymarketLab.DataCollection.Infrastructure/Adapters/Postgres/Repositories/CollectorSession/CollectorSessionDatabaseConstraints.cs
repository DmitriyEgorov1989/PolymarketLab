namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.CollectorSession;

internal static class CollectorSessionDatabaseConstraints
{
    public const string ExclusiveSlot = "ux_collector_sessions_exclusive_slot";
    public const string ExclusiveSlotProperty = "ExclusiveSlot";
    public const string ExclusiveStatusFilter = "\"status\" IN (0, 1, 2, 6, 7)";
    public const string ExclusiveSlotCheck = "ck_collector_sessions_exclusive_slot";

    public static bool IsExclusiveSlotConstraint(string? constraintName)
    {
        return constraintName == ExclusiveSlot;
    }
}
