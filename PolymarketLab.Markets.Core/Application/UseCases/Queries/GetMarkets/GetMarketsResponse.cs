using PolymarketLab.Markets.Core.Application.UseCases.Common;

namespace PolymarketLab.Markets.Core.Application.UseCases.Queries.GetMarkets;

public sealed record GetMarketsResponse(
    IReadOnlyCollection<MarketResponse> Markets);
