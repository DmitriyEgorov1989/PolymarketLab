using PolymarketLab.DataCollection.Core.Application.UseCases.Common;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Queries.GetCollectorSessionById;

public sealed record GetCollectorSessionByIdResponse(
    CollectorSessionResponse Session);
