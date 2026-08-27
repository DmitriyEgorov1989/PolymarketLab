namespace PolymarketLab.DataCollection.Core.Ports.Dtos;

/// <summary>Результат проверки terminal resolution в Gamma.</summary>
public enum GammaTerminalResolutionStatus
{
    /// <summary>Gamma ещё не подтверждает окончательное разрешение рынка.</summary>
    NonTerminal = 0,

    /// <summary>Gamma подтверждает окончательное разрешение и единственного победителя.</summary>
    Terminal = 1
}

/// <summary>Проверенное безопасное наблюдение состояния разрешения Gamma.</summary>
/// <param name="ObservedAt">Локальное UTC-время завершения проверки.</param>
/// <param name="ExternalEventId">Проверенный внешний идентификатор события.</param>
/// <param name="EventSlug">Проверенный slug события.</param>
/// <param name="ExternalMarketId">Проверенный внешний идентификатор рынка.</param>
/// <param name="MarketSlug">Проверенный slug рынка.</param>
/// <param name="ConditionId">Проверенный идентификатор условия рынка.</param>
/// <param name="Closed">Признак закрытия рынка в Gamma.</param>
/// <param name="AcceptingOrders">Признак приёма новых ордеров в Gamma.</param>
/// <param name="UmaResolutionStatus">Статус разрешения UMA либо <see langword="null"/>, если Gamma его ещё не предоставила.</param>
/// <param name="ExternalClosedAt">Внешнее время закрытия либо <see langword="null"/>, если Gamma его не предоставила.</param>
/// <param name="Status">Результат проверки terminal-признаков и цен.</param>
/// <param name="Outcomes">Проверенные исходы, токены и их текущие цены.</param>
/// <param name="Winner">Единственный победитель либо <see langword="null"/> для non-terminal наблюдения.</param>
public sealed record GammaTerminalResolutionObservation(
    DateTimeOffset ObservedAt,
    string ExternalEventId,
    string EventSlug,
    string ExternalMarketId,
    string MarketSlug,
    string ConditionId,
    bool Closed,
    bool AcceptingOrders,
    string? UmaResolutionStatus,
    DateTimeOffset? ExternalClosedAt,
    GammaTerminalResolutionStatus Status,
    IReadOnlyCollection<GammaResolutionOutcome> Outcomes,
    GammaResolutionOutcome? Winner);

/// <summary>Проверенные данные одного исхода Gamma.</summary>
/// <param name="TokenId">Внешний идентификатор токена.</param>
/// <param name="Outcome">Название исхода.</param>
/// <param name="OutcomeIndex">Позиция исхода во внешнем рынке.</param>
/// <param name="Price">Цена исхода от <c>0.00</c> до <c>1.00</c>.</param>
public sealed record GammaResolutionOutcome(
    string TokenId,
    string Outcome,
    int OutcomeIndex,
    decimal Price);
