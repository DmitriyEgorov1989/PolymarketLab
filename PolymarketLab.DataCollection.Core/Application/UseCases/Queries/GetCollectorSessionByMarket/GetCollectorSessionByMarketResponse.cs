using PolymarketLab.DataCollection.Core.Application.UseCases.Common;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Queries.GetCollectorSessionByMarket;

public sealed record GetCollectorSessionByMarketResponse(
    CollectorSessionResponse? Session);
