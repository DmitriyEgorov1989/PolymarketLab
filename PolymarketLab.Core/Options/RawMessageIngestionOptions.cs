namespace PolymarketLab.Core.Options;

public sealed class RawMessageIngestionOptions
{
    public const string SectionName = "RawMessageIngestion";
    public static readonly TimeSpan MaximumFlushInterval =
        TimeSpan.FromMilliseconds(uint.MaxValue - 1);
    public static readonly TimeSpan MaximumShutdownTimeout =
        TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    public int Capacity { get; init; } = 10_000;
    public int BatchSize { get; init; } = 500;
    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(10);
}
