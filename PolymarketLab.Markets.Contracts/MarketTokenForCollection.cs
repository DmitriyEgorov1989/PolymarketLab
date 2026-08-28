using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.Markets.Contracts;

/// <summary>Представляет токен исхода в проверенном снимке рынка.</summary>
/// <param name="TokenId">Внешний идентификатор токена.</param>
/// <param name="Outcome">Непустое название исхода.</param>
/// <param name="OutcomeIndex">Позиция исхода в порядке Gamma.</param>
public sealed record MarketTokenForCollection(
    TokenId TokenId,
    string Outcome,
    int OutcomeIndex);
