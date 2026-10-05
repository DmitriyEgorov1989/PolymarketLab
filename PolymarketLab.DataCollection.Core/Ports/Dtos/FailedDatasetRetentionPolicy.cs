namespace PolymarketLab.DataCollection.Core.Ports.Dtos;

/// <summary>Неизменяемый снимок политики хранения набора данных после ошибки.</summary>
/// <param name="Enabled">Признак сохранения набора данных после ошибки.</param>
/// <param name="RetentionPeriod">Период хранения набора данных после ошибки.</param>
/// <param name="MaximumRetainedSessions">Максимальное количество одновременно хранимых диагностических сессий.</param>
public sealed record FailedDatasetRetentionPolicy(
    bool Enabled,
    TimeSpan RetentionPeriod,
    int MaximumRetainedSessions);
