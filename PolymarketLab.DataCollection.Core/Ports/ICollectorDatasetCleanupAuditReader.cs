using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports;

/// <summary>Читает долговечное свидетельство завершённой очистки dataset сессии.</summary>
public interface ICollectorDatasetCleanupAuditReader
{
    /// <summary>Получает audit очистки сессии.</summary>
    /// <param name="sessionId">Идентификатор сессии.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Audit очистки либо <see langword="null" />, если очистка не выполнялась.</returns>
    Task<CollectorDatasetCleanupAudit?> GetBySessionIdAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken);
}
