namespace PolymarketLab.DataCollection.Core.Application.UseCases.Common;

/// <summary>Безопасный HTTP-снимок lifecycle и evidence одной коллекторной сессии.</summary>
/// <param name="SessionId">Идентификатор сессии.</param>
/// <param name="MarketId">Идентификатор зарегистрированного рынка.</param>
/// <param name="Snapshot">Снимок рынка; всегда присутствует, члены identity/window/version nullable у legacy session.</param>
/// <param name="Status">Строковое имя lifecycle-состояния сессии.</param>
/// <param name="Phase">Строковое имя точной фазы нетерминальной сессии; <see langword="null" /> для terminal и legacy session.</param>
/// <param name="EffectiveDeadline">Граница текущей фазы с фиксированным сроком; <see langword="null" /> без фиксированной границы.</param>
/// <param name="CreatedAt">Дата и время создания сессии.</param>
/// <param name="StartedAt">Начало preparation; <see langword="null" />, если preparation не начиналась.</param>
/// <param name="SubscriptionReadyAt">Момент доказанной готовности подписки; <see langword="null" />, пока readiness не доказана.</param>
/// <param name="StoppedAt">Дата завершения; <see langword="null" />, пока session нетерминальна.</param>
/// <param name="InvalidatingAt">Момент установки durable write fence; <see langword="null" />, если invalidation не начиналась.</param>
/// <param name="StopReason">Строковое имя причины terminal transition; <see langword="null" />, пока session нетерминальна.</param>
/// <param name="FailureCode">Машиночитаемый код failure; <see langword="null" /> при отсутствии failure.</param>
/// <param name="FailureMessage">Безопасное описание failure; <see langword="null" /> при отсутствии failure.</param>
/// <param name="Readiness">Готовность подписки текущей connection epoch.</param>
/// <param name="MessagesReceived">Историческое количество полностью полученных сообщений.</param>
/// <param name="MessagesEnqueued">Историческое количество сообщений, переданных в bounded ingestion.</param>
/// <param name="MessagesPersisted">Историческое количество подтверждённых PostgreSQL сообщений.</param>
/// <param name="RemainingRawMessageCount">Авторитетное текущее количество raw-сообщений сессии в PostgreSQL.</param>
/// <param name="LastMessageAt">Момент получения последнего сообщения.</param>
/// <param name="ReconnectCount">Количество повторных подключений.</param>
/// <param name="Normalization">Текущие remaining counts snapshot-версии; <see langword="null" /> после cleanup либо у legacy session без версии.</param>
/// <param name="Resolution">Состояние разрешения рынка; всегда присутствует.</param>
/// <param name="Cleanup">Свидетельство завершённой очистки dataset; <see langword="null" /> до committed cleanup.</param>
public sealed record CollectorSessionResponse(
    Guid SessionId,
    Guid MarketId,
    CollectorSessionSnapshotResponse Snapshot,
    string Status,
    string? Phase,
    DateTimeOffset? EffectiveDeadline,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? SubscriptionReadyAt,
    DateTimeOffset? StoppedAt,
    DateTimeOffset? InvalidatingAt,
    string? StopReason,
    string? FailureCode,
    string? FailureMessage,
    CollectorReadinessResponse Readiness,
    long MessagesReceived,
    long MessagesEnqueued,
    long MessagesPersisted,
    long RemainingRawMessageCount,
    DateTimeOffset? LastMessageAt,
    long ReconnectCount,
    CollectorNormalizationResponse? Normalization,
    CollectorResolutionResponse Resolution,
    CollectorCleanupResponse? Cleanup);
