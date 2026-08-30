using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Ports;

/// <summary>Сохраняет и загружает агрегаты сессий сборщика.</summary>
public interface ICollectorSessionRepository
{
    /// <summary>Получает сессию по идентификатору.</summary>
    /// <param name="sessionId">Идентификатор сессии.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Сессия либо <see langword="null" />, если она не найдена.</returns>
    Task<CollectorSession?> GetByIdAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken);

    /// <summary>Получает единственную сессию, занимающую глобальный exclusive slot.</summary>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Exclusive session либо <see langword="null" />, если slot свободен.</returns>
    Task<CollectorSession?> GetExclusiveAsync(CancellationToken cancellationToken);

    /// <summary>Получает активную сессию рынка.</summary>
    /// <param name="marketId">Идентификатор рынка.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Активная сессия либо <see langword="null" />.</returns>
    Task<CollectorSession?> GetActiveByMarketIdAsync(
        MarketId marketId,
        CancellationToken cancellationToken);

    /// <summary>Получает активную или последнюю созданную сессию рынка.</summary>
    /// <param name="marketId">Идентификатор рынка.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Текущая сессия либо <see langword="null" />, если история отсутствует.</returns>
    Task<CollectorSession?> GetCurrentByMarketIdAsync(
        MarketId marketId,
        CancellationToken cancellationToken);

    /// <summary>Получает все активные сессии.</summary>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Снимок активных сессий.</returns>
    Task<IReadOnlyCollection<CollectorSession>> GetActiveAsync(
        CancellationToken cancellationToken);

    /// <summary>Пытается добавить новую сессию с учётом глобального exclusive slot.</summary>
    /// <param name="session">Добавляемая сессия.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Статус вставки либо ошибка persistence.</returns>
    Task<Result<CollectorSessionInsertStatus, Error>> TryAddAsync(
        CollectorSession session,
        CancellationToken cancellationToken);

    /// <summary>Пытается сохранить сессию при совпадении ожидаемого исходного статуса.</summary>
    /// <param name="session">Изменённый агрегат сессии.</param>
    /// <param name="expectedStatus">Статус, ожидаемый в persistence.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Статус обновления либо ошибка persistence.</returns>
    Task<Result<CollectorSessionUpdateStatus, Error>> TryUpdateAsync(
        CollectorSession session,
        CollectorSessionStatus expectedStatus,
        CancellationToken cancellationToken);
}
