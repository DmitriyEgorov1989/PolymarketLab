namespace PolymarketLab.DataCollection.Core.Ports.Dtos;

/// <summary>Ожидаемая identity рынка для проверки terminal resolution в Gamma.</summary>
/// <param name="ExternalEventId">Внешний идентификатор события.</param>
/// <param name="EventSlug">Slug события.</param>
/// <param name="ExternalMarketId">Внешний идентификатор рынка.</param>
/// <param name="MarketSlug">Slug рынка.</param>
/// <param name="ConditionId">Идентификатор условия рынка.</param>
/// <param name="Tokens">Упорядоченная identity токенов исходов.</param>
public sealed record GammaTerminalResolutionRequest(
    string ExternalEventId,
    string EventSlug,
    string ExternalMarketId,
    string MarketSlug,
    string ConditionId,
    IReadOnlyCollection<GammaResolutionTokenIdentity> Tokens);

/// <summary>Ожидаемая identity одного токена исхода.</summary>
/// <param name="TokenId">Внешний идентификатор токена.</param>
/// <param name="Outcome">Название исхода.</param>
/// <param name="OutcomeIndex">Позиция исхода во внешнем рынке.</param>
public sealed record GammaResolutionTokenIdentity(
    string TokenId,
    string Outcome,
    int OutcomeIndex);
