namespace PolymarketLab.Markets.Core.Application.UseCases.Commands;

public sealed record RegisterMarketResponse(
    Guid MarketId,
    bool Created);
