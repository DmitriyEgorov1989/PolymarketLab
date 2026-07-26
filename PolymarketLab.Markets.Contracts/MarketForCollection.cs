using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.Markets.Contracts;

public sealed record MarketForCollection(
    MarketId MarketId,
    string Slug,
    IReadOnlyCollection<MarketTokenForCollection> Tokens);
