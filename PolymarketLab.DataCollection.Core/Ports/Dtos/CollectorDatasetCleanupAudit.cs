using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports.Dtos;

/// <summary>Содержит долговечное свидетельство удаления перестраиваемых данных сессии.</summary>
/// <param name="SessionId">Идентификатор очищенной сессии.</param>
/// <param name="CompletedAt">Момент успешного завершения очистки.</param>
/// <param name="DeletedRawMessageCount">Количество удалённых исходных сообщений.</param>
/// <param name="DeletedNormalizationCount">Количество удалённых записей журнала нормализации всех версий.</param>
/// <param name="DeletedNormalizedEventCount">Количество удалённых нормализованных событий всех версий.</param>
public sealed record CollectorDatasetCleanupAudit(
    CollectorSessionId SessionId,
    DateTimeOffset CompletedAt,
    long DeletedRawMessageCount,
    long DeletedNormalizationCount,
    long DeletedNormalizedEventCount);
