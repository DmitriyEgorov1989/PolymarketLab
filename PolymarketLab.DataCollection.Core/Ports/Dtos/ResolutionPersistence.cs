using PolymarketLab.DataCollection.Core.Application.Resolution;
using PolymarketLab.DataCollection.Core.Domain.Models.Resolution;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports.Dtos;

/// <summary>Источник устойчивого наблюдения разрешения рынка.</summary>
public enum ResolutionObservationSource
{
    /// <summary>Сохранённое сообщение WebSocket.</summary>
    WebSocket = 0,

    /// <summary>Проверка Gamma API.</summary>
    Gamma = 1,

    /// <summary>Проверка CLOB API.</summary>
    Clob = 2
}

/// <summary>Безопасный статус устойчивого наблюдения разрешения рынка.</summary>
public enum DurableResolutionObservationStatus
{
    /// <summary>Наблюдение WebSocket отклонено без terminal conflict.</summary>
    Rejected = 0,

    /// <summary>Внешний источник ещё не подтверждает terminal state.</summary>
    NonTerminal = 1,

    /// <summary>Источник подтвердил terminal state и единственного победителя.</summary>
    Terminal = 2,

    /// <summary>Проверка источника завершилась безопасно описанной ошибкой.</summary>
    Failed = 3,

    /// <summary>Проверенные данные источника противоречат снимку сессии или другому источнику.</summary>
    Conflict = 4
}

/// <summary>Результат чтения очередного batch сохранённых WebSocket-сообщений.</summary>
/// <param name="LastScannedRawMessageId">Максимальный просмотренный raw message id либо исходный cursor, если новых строк нет.</param>
/// <param name="Candidates">Кандидаты <c>market_resolved</c>, найденные в просмотренных строках.</param>
public sealed record WebSocketResolutionScanResult(
    long LastScannedRawMessageId,
    IReadOnlyCollection<WebSocketResolutionCandidate> Candidates);

/// <summary>Безопасный результат strict validation одного WebSocket-кандидата.</summary>
/// <param name="Candidate">Кандидат с provenance исходного сообщения.</param>
/// <param name="Status">Результат проверки кандидата.</param>
/// <param name="Winner">Проверенный победитель либо <see langword="null" />, если победитель не подтверждён.</param>
/// <param name="ErrorCode">Безопасный код отклонения или конфликта либо <see langword="null" />.</param>
/// <param name="ErrorMessage">Безопасное сообщение об ошибке либо <see langword="null" />.</param>
public sealed record DurableWebSocketResolutionValidation(
    WebSocketResolutionCandidate Candidate,
    DurableResolutionObservationStatus Status,
    ResolutionWinner? Winner,
    string? ErrorCode,
    string? ErrorMessage);

/// <summary>Атомарная запись результата WebSocket scan и нового cursor.</summary>
/// <param name="SessionId">Идентификатор сессии.</param>
/// <param name="LastScannedRawMessageId">Максимальный raw message id, просмотренный scanner.</param>
/// <param name="Validations">Strict validation найденных кандидатов.</param>
public sealed record DurableWebSocketResolutionScan(
    CollectorSessionId SessionId,
    long LastScannedRawMessageId,
    IReadOnlyCollection<DurableWebSocketResolutionValidation> Validations);

/// <summary>Безопасная ошибка проверки внешнего resolution source.</summary>
/// <param name="SessionId">Идентификатор сессии.</param>
/// <param name="Source">Источник, проверка которого завершилась ошибкой.</param>
/// <param name="ObservedAt">Локальное UTC-время завершения проверки.</param>
/// <param name="ErrorCode">Безопасный код ошибки.</param>
/// <param name="ErrorMessage">Безопасное сообщение ошибки без raw payload.</param>
public sealed record DurableResolutionFailure(
    CollectorSessionId SessionId,
    ResolutionObservationSource Source,
    DateTimeOffset ObservedAt,
    string ErrorCode,
    string ErrorMessage);

/// <summary>Ссылка на два устойчивых наблюдения, образующих подтверждение resolution.</summary>
/// <param name="PrimaryObservationId">Идентификатор первого terminal observation.</param>
/// <param name="ConfirmingObservationId">Идентификатор согласованного подтверждающего observation.</param>
/// <param name="ConfirmedAt">Локальное UTC-время формирования подтверждения.</param>
public sealed record ResolutionConfirmationReference(
    long PrimaryObservationId,
    long ConfirmingObservationId,
    DateTimeOffset ConfirmedAt);

/// <summary>Безопасный сохранённый исход resolution observation.</summary>
/// <param name="OutcomeIndex">Позиция исхода в проверенном источнике.</param>
/// <param name="TokenId">Проверенный внешний идентификатор токена.</param>
/// <param name="Outcome">Название исхода либо <see langword="null" />, если WebSocket его не предоставил.</param>
/// <param name="Price">Цена от <c>0.00</c> до <c>1.00</c> либо <see langword="null" /> для WebSocket.</param>
/// <param name="IsWinner">Признак проверенного победителя.</param>
public sealed record DurableResolutionOutcome(
    int OutcomeIndex,
    string TokenId,
    string? Outcome,
    decimal? Price,
    bool IsWinner);

/// <summary>Безопасное устойчивое resolution observation без raw payload.</summary>
/// <param name="Id">Идентификатор observation для confirmation reference.</param>
/// <param name="Source">Источник observation.</param>
/// <param name="ObservedAt">Локальное UTC-время observation.</param>
/// <param name="Status">Проверенный статус observation.</param>
/// <param name="Winner">Проверенный победитель либо <see langword="null" />.</param>
/// <param name="ExternalEventId">Проверенный Gamma event id либо <see langword="null" />.</param>
/// <param name="EventSlug">Проверенный Gamma event slug либо <see langword="null" />.</param>
/// <param name="ExternalMarketId">Проверенный внешний market id либо <see langword="null" />.</param>
/// <param name="MarketSlug">Проверенный Gamma market slug либо <see langword="null" />.</param>
/// <param name="ConditionId">Проверенный condition id либо <see langword="null" />.</param>
/// <param name="Closed">Проверенный terminal flag либо <see langword="null" /> для WebSocket.</param>
/// <param name="AcceptingOrders">Проверенный order flag либо <see langword="null" /> для WebSocket.</param>
/// <param name="UmaResolutionStatus">Безопасный Gamma UMA status либо <see langword="null" />.</param>
/// <param name="ExternalClosedAt">Внешнее время закрытия либо <see langword="null" />.</param>
/// <param name="ErrorCode">Безопасный код ошибки либо <see langword="null" />.</param>
/// <param name="ErrorMessage">Безопасное сообщение ошибки либо <see langword="null" />.</param>
/// <param name="RawMessageId">WS raw message id либо <see langword="null" /> для внешнего source.</param>
/// <param name="RawItemIndex">WS raw item index либо <see langword="null" /> для внешнего source.</param>
/// <param name="ConnectionEpoch">WS connection epoch либо <see langword="null" /> для внешнего source.</param>
/// <param name="Outcomes">Проверенные безопасные исходы observation.</param>
public sealed record DurableResolutionObservation(
    long Id,
    ResolutionObservationSource Source,
    DateTimeOffset ObservedAt,
    DurableResolutionObservationStatus Status,
    ResolutionWinner? Winner,
    string? ExternalEventId,
    string? EventSlug,
    string? ExternalMarketId,
    string? MarketSlug,
    string? ConditionId,
    bool? Closed,
    bool? AcceptingOrders,
    string? UmaResolutionStatus,
    DateTimeOffset? ExternalClosedAt,
    string? ErrorCode,
    string? ErrorMessage,
    long? RawMessageId,
    int? RawItemIndex,
    long? ConnectionEpoch,
    IReadOnlyCollection<DurableResolutionOutcome> Outcomes);

/// <summary>Устойчивое состояние resolution scanner и polling для сессии.</summary>
/// <param name="SessionId">Идентификатор сессии.</param>
/// <param name="LastScannedRawMessageId">Последний просмотренный raw message id; 0 означает отсутствие scan.</param>
/// <param name="LastPollingCycleAt">Время последнего начатого polling cycle либо <see langword="null" />.</param>
/// <param name="Confirmation">Ссылка на подтверждающие observations либо <see langword="null" />.</param>
/// <param name="Observations">Все безопасные observations сессии в порядке сохранения.</param>
public sealed record DurableResolutionState(
    CollectorSessionId SessionId,
    long LastScannedRawMessageId,
    DateTimeOffset? LastPollingCycleAt,
    ResolutionConfirmationReference? Confirmation,
    IReadOnlyCollection<DurableResolutionObservation> Observations);
