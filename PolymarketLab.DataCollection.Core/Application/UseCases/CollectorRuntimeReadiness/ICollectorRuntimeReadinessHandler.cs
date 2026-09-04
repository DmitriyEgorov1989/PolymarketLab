using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorRuntimeReadiness;

/// <summary>Сохраняет durable-фазы readiness, которые наблюдает process-local runtime.</summary>
public interface ICollectorRuntimeReadinessHandler
{
    /// <summary>Отмечает, что текущая WebSocket epoch ждёт initial books.</summary>
    /// <param name="sessionId">Идентификатор сессии.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    Task<UnitResult<Error>> MarkAwaitingInitialBooksAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken);

    /// <summary>Отмечает, что все initial books текущей epoch enqueued и runtime ждёт heartbeat.</summary>
    /// <param name="sessionId">Идентификатор сессии.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    Task<UnitResult<Error>> MarkAwaitingHeartbeatAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken);

    /// <summary>Фиксирует доказанную готовность подписки.</summary>
    /// <param name="sessionId">Идентификатор сессии.</param>
    /// <param name="subscriptionReadyAt">UTC-момент получения подтверждающего PONG.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    Task<UnitResult<Error>> MarkRunningAsync(
        CollectorSessionId sessionId,
        DateTimeOffset subscriptionReadyAt,
        CancellationToken cancellationToken);

    /// <summary>Начинает invalidation после недоказанной readiness или нарушения непрерывности.</summary>
    /// <param name="sessionId">Идентификатор сессии.</param>
    /// <param name="failure">Безопасная причина нарушения; <see langword="null" /> не допускается.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    Task<UnitResult<Error>> BeginInvalidationAsync(
        CollectorSessionId sessionId,
        Error failure,
        CancellationToken cancellationToken);

    /// <summary>
    /// Сохраняет durable-наблюдение успешной постановки initial book одного токена
    /// текущей connection epoch.
    /// </summary>
    /// <param name="sessionId">Идентификатор сессии.</param>
    /// <param name="tokenId">Внешний идентификатор токена из immutable snapshot.</param>
    /// <param name="connectionEpoch">Положительная connection epoch наблюдения.</param>
    /// <param name="enqueuedAt">UTC-момент успешной постановки в bounded ingestion.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    Task<UnitResult<Error>> RecordInitialBookEnqueuedAsync(
        CollectorSessionId sessionId,
        TokenId tokenId,
        long connectionEpoch,
        DateTimeOffset enqueuedAt,
        CancellationToken cancellationToken);
}
