namespace PolymarketLab.DataCollection.Core.Ports.Dtos;

/// <summary>Ожидаемая identity рынка для проверки terminal resolution в CLOB.</summary>
/// <param name="ConditionId">Идентификатор условия рынка.</param>
/// <param name="Tokens">Упорядоченная identity токенов исходов.</param>
public sealed record ClobTerminalResolutionRequest(
    string ConditionId,
    IReadOnlyCollection<ClobResolutionTokenIdentity> Tokens);

/// <summary>Ожидаемая identity одного токена исхода.</summary>
/// <param name="TokenId">Внешний идентификатор токена.</param>
/// <param name="Outcome">Название исхода.</param>
/// <param name="OutcomeIndex">Позиция исхода во внешнем рынке.</param>
public sealed record ClobResolutionTokenIdentity(
    string TokenId,
    string Outcome,
    int OutcomeIndex);
