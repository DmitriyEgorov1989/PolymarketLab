using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

/// <summary>Хранит долговечное свидетельство завершённой очистки dataset.</summary>
internal sealed class CollectorDatasetCleanupAuditRecord
{
    private CollectorDatasetCleanupAuditRecord()
    {
    }

    /// <summary>Создаёт persistence-запись из результата очистки.</summary>
    public CollectorDatasetCleanupAuditRecord(CollectorDatasetCleanupAudit audit)
    {
        SessionId = audit.SessionId;
        CompletedAt = audit.CompletedAt;
        DeletedRawMessageCount = audit.DeletedRawMessageCount;
        DeletedNormalizationCount = audit.DeletedNormalizationCount;
        DeletedNormalizedEventCount = audit.DeletedNormalizedEventCount;
    }

    /// <summary>Идентификатор очищенной сессии.</summary>
    public CollectorSessionId SessionId { get; private set; } = null!;

    /// <summary>Момент успешного завершения очистки.</summary>
    public DateTimeOffset CompletedAt { get; private set; }

    /// <summary>Количество удалённых исходных сообщений.</summary>
    public long DeletedRawMessageCount { get; private set; }

    /// <summary>Количество удалённых записей журнала нормализации.</summary>
    public long DeletedNormalizationCount { get; private set; }

    /// <summary>Количество удалённых нормализованных событий.</summary>
    public long DeletedNormalizedEventCount { get; private set; }

    /// <summary>Преобразует persistence-запись в DTO порта.</summary>
    public CollectorDatasetCleanupAudit ToAudit() => new(
        SessionId,
        CompletedAt,
        DeletedRawMessageCount,
        DeletedNormalizationCount,
        DeletedNormalizedEventCount);
}
