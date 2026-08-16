namespace PolymarketLab.Core.Options;

public sealed class NormalizerOptions
{
    public const string SectionName = "Normalizer";
    public static readonly TimeSpan MaximumShutdownTimeout = TimeSpan.FromMinutes(5);

    public bool Enabled { get; init; } = true;
    public int ProjectionVersion { get; init; } = 1;
    public int BatchSize { get; init; } = 500;
    public TimeSpan IdleDelay { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan ClaimTimeout { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
