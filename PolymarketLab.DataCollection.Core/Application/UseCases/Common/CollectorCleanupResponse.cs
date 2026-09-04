namespace PolymarketLab.DataCollection.Core.Application.UseCases.Common;

/// <summary>Свидетельство завершённой очистки dataset для HTTP-ответа.</summary>
/// <param name="InvalidatingAt">Момент установки durable write fence; <see langword="null" /> для legacy session.</param>
/// <param name="CleanedAt">Момент успешного завершения очистки.</param>
/// <param name="ProjectionVersion">Сохранённая snapshot-версия очищенной сессии; <see langword="null" /> для legacy session.</param>
/// <param name="FailureCode">Сохранённый код failure либо <see langword="null" />.</param>
/// <param name="FailureMessage">Сохранённое безопасное описание failure либо <see langword="null" />.</param>
/// <param name="DeletedRawMessageCount">Количество удалённых исходных сообщений.</param>
/// <param name="DeletedNormalizationCount">Количество удалённых записей журнала нормализации всех версий.</param>
/// <param name="DeletedNormalizedEventCount">Количество удалённых нормализованных событий всех версий.</param>
public sealed record CollectorCleanupResponse(
    DateTimeOffset? InvalidatingAt,
    DateTimeOffset CleanedAt,
    int? ProjectionVersion,
    string? FailureCode,
    string? FailureMessage,
    long DeletedRawMessageCount,
    long DeletedNormalizationCount,
    long DeletedNormalizedEventCount);
