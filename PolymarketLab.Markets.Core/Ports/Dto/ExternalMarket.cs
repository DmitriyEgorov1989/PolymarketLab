namespace PolymarketLab.Markets.Core.Ports.Dto;

/// <summary>
///     Представляет нормализованные данные дочернего рынка из внешнего источника.
/// </summary>
/// <param name="ExternalMarketId">Внешний идентификатор рынка.</param>
/// <param name="Slug">Slug дочернего рынка, который может отличаться от slug события.</param>
/// <param name="Question">Вопрос рынка.</param>
/// <param name="ConditionId">Идентификатор condition рынка.</param>
/// <param name="ExternalCreatedAt">Внешнее время создания либо <see langword="null"/>, если значение отсутствует.</param>
/// <param name="OrdersOpenedAt">Историческое время открытия заявок либо <see langword="null"/>, если значение отсутствует.</param>
/// <param name="GammaStartDate">Gamma <c>startDate</c> либо <see langword="null"/>, если значение отсутствует.</param>
/// <param name="EventStartsAt">Начало предметного окна либо <see langword="null"/>, если значение отсутствует.</param>
/// <param name="EventEndsAt">Конец предметного окна либо <see langword="null"/>, если значение отсутствует.</param>
/// <param name="ExternalClosedAt">Внешнее время закрытия либо <see langword="null"/>, если значение отсутствует.</param>
/// <param name="UmaResolutionStatus">Статус resolution UMA либо <see langword="null"/>, если значение отсутствует.</param>
/// <param name="Active">Признак активности внешнего рынка.</param>
/// <param name="Closed">Признак закрытия внешнего рынка.</param>
/// <param name="AcceptingOrders">Признак текущего приёма заявок.</param>
/// <param name="OrderBookEnabled">Признак включённого CLOB order book.</param>
/// <param name="Tokens">Outcome tokens в порядке внешнего источника.</param>
public sealed record ExternalMarket(
    string ExternalMarketId,
    string Slug,
    string Question,
    string ConditionId,
    DateTimeOffset? ExternalCreatedAt,
    DateTimeOffset? OrdersOpenedAt,
    DateTimeOffset? GammaStartDate,
    DateTimeOffset? EventStartsAt,
    DateTimeOffset? EventEndsAt,
    DateTimeOffset? ExternalClosedAt,
    string? UmaResolutionStatus,
    bool Active,
    bool Closed,
    bool AcceptingOrders,
    bool OrderBookEnabled,
    IReadOnlyList<ExternalMarketToken> Tokens);

/// <summary>
///     Представляет одно упорядоченное соответствие outcome и token внешнего рынка.
/// </summary>
/// <param name="Outcome">Внешнее название outcome.</param>
/// <param name="TokenId">Внешний идентификатор token.</param>
/// <param name="OutcomeIndex">Позиция во внешних массивах outcomes с нулевой индексацией.</param>
public sealed record ExternalMarketToken(string Outcome, string TokenId, int OutcomeIndex);
