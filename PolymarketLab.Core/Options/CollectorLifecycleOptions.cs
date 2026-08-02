namespace PolymarketLab.Core.Options;

public sealed class CollectorLifecycleOptions
{
    public const string SectionName = "CollectorLifecycle";
    public static readonly TimeSpan MaximumShutdownTimeout = TimeSpan.FromMinutes(5);

    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
