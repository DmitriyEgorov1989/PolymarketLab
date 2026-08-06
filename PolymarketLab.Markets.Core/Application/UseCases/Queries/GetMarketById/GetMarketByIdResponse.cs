using PolymarketLab.Markets.Core.Application.UseCases.Common;

namespace PolymarketLab.Markets.Core.Application.UseCases.Queries.GetMarketById;

public sealed record GetMarketByIdResponse(
    MarketResponse Market);
