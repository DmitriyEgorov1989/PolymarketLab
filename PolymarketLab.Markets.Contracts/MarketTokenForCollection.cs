using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.Markets.Contracts;

public sealed record MarketTokenForCollection(
    TokenId TokenId,
    string Outcome,
    int OutcomeIndex);
