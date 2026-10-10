namespace PolymarketLab.Core.Options;

public sealed class NormalizerOptions
{
    public const string SectionName = "Normalizer";

    /// <summary>Максимально допустимое количество параллельных обработчиков.</summary>
    public const int MaximumWorkerCount = 16;
    public static readonly TimeSpan MaximumShutdownTimeout = TimeSpan.FromMinutes(5);

    public bool Enabled { get; init; } = true;
    public int ProjectionVersion { get; init; } = 1;

    /// <summary>Количество параллельных обработчиков разных collector sessions.</summary>
    public int WorkerCount { get; init; } = 3;
    public int BatchSize { get; init; } = 500;

    /// <summary>Максимальное количество исходных сообщений в одной транзакции записи.</summary>
    public int WriteBatchSize { get; init; } = 100;
    public TimeSpan IdleDelay { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan ClaimTimeout { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
