namespace PolymarketLab.DataCollection.Core.Ports.Dtos;

/// <summary>Результат проверки terminal resolution в CLOB.</summary>
public enum ClobTerminalResolutionStatus
{
    /// <summary>CLOB ещё не подтверждает окончательное разрешение рынка.</summary>
    NonTerminal = 0,

    /// <summary>CLOB подтверждает окончательное разрешение и единственного победителя.</summary>
    Terminal = 1
}

/// <summary>Проверенное безопасное наблюдение состояния разрешения CLOB.</summary>
/// <param name="ObservedAt">Локальное UTC-время завершения проверки.</param>
/// <param name="ConditionId">Проверенный идентификатор условия рынка.</param>
/// <param name="Closed">Признак закрытия рынка в CLOB.</param>
/// <param name="AcceptingOrders">Признак приёма новых ордеров в CLOB.</param>
/// <param name="Status">Результат проверки terminal-признаков и цен.</param>
/// <param name="Outcomes">Проверенные исходы, токены и их текущие цены.</param>
/// <param name="Winner">Единственный победитель либо <see langword="null"/> для non-terminal наблюдения.</param>
public sealed record ClobTerminalResolutionObservation(
    DateTimeOffset ObservedAt,
    string ConditionId,
    bool Closed,
    bool AcceptingOrders,
    ClobTerminalResolutionStatus Status,
    IReadOnlyCollection<ClobResolutionOutcome> Outcomes,
    ClobResolutionOutcome? Winner);

/// <summary>Проверенные данные одного исхода CLOB.</summary>
/// <param name="TokenId">Внешний идентификатор токена.</param>
/// <param name="Outcome">Название исхода.</param>
/// <param name="OutcomeIndex">Позиция исхода в snapshot сессии.</param>
/// <param name="Price">Цена исхода от <c>0.00</c> до <c>1.00</c>.</param>
public sealed record ClobResolutionOutcome(
    string TokenId,
    string Outcome,
    int OutcomeIndex,
    decimal Price);
