namespace PolymarketLab.Core.Options;

/// <summary>Параметры хранения наборов данных диагностических сессий после ошибки.</summary>
public sealed class FailedDatasetRetentionOptions
{
    /// <summary>Название секции конфигурации.</summary>
    public const string SectionName = "FailedDatasetRetention";

    /// <summary>Признак сохранения набора данных сессии после ошибки.</summary>
    public bool Enabled { get; init; }

    /// <summary>Период хранения набора данных после ошибки.</summary>
    public TimeSpan RetentionPeriod { get; init; } = TimeSpan.FromHours(12);

    /// <summary>Максимальное количество одновременно хранимых диагностических сессий.</summary>
    public int MaximumRetainedSessions { get; init; } = 5;
}
