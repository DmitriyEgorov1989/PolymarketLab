using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports;

/// <summary>Сохраняет и читает compact durable-наблюдения готовности токенов по connection epoch.</summary>
public interface ICollectorTokenReadinessRepository
{
    /// <summary>
    /// Сохраняет наблюдение успешной постановки initial book; повторная запись того же
    /// <c>(SessionId, ConnectionEpoch, TokenId)</c> идемпотентна и сохраняет первый timestamp.
    /// </summary>
    /// <param name="readiness">Наблюдение готовности токена.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    Task RecordInitialBookEnqueuedAsync(
        CollectorTokenReadiness readiness,
        CancellationToken cancellationToken);

    /// <summary>Получает готовность токенов указанной connection epoch.</summary>
    /// <param name="sessionId">Идентификатор сессии.</param>
    /// <param name="connectionEpoch">Положительная connection epoch.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Наблюдения готовности указанной epoch.</returns>
    Task<IReadOnlyCollection<CollectorTokenReadiness>> GetAsync(
        CollectorSessionId sessionId,
        long connectionEpoch,
        CancellationToken cancellationToken);
}
