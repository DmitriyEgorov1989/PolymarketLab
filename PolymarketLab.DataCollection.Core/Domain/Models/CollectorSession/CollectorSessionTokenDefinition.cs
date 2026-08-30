using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;

/// <summary>Неизменяемое определение token outcome для создания snapshot сессии.</summary>
/// <param name="TokenId">Внешний идентификатор токена.</param>
/// <param name="Outcome">Название исхода.</param>
/// <param name="OutcomeIndex">Позиция исхода во внешнем рынке.</param>
public sealed record CollectorSessionTokenDefinition(
    TokenId TokenId,
    string Outcome,
    int OutcomeIndex);
