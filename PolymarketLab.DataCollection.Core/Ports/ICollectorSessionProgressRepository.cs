using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports;

/// <summary>Читает и сохраняет устойчивый прогресс сессии сборщика.</summary>
public interface ICollectorSessionProgressRepository
{
    /// <summary>Получает сохранённую epoch, счётчики, время последнего сообщения и авторитетное количество raw rows.</summary>
    /// <param name="sessionId">Идентификатор сессии.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Сохранённый прогресс сессии.</returns>
    Task<CollectorSessionProgress> GetAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken);

    /// <summary>Монотонно обновляет checkpoint прогресса сессии.</summary>
    /// <param name="checkpoint">Новый снимок epoch, счётчиков сообщений и подключений.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    Task CheckpointAsync(
        CollectorSessionProgressCheckpoint checkpoint,
        CancellationToken cancellationToken);
}
