namespace PolymarketLab.DataCollection.Core.Domain.Models.Resolution;

/// <summary>Проверенный выигравший token/outcome snapshot рынка.</summary>
/// <param name="TokenId">Внешний идентификатор выигравшего токена.</param>
/// <param name="Outcome">Название выигравшего исхода.</param>
public sealed record ResolutionWinner(string TokenId, string Outcome);
