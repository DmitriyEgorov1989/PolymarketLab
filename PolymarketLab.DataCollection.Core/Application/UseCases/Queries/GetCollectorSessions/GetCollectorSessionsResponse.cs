using PolymarketLab.DataCollection.Core.Application.UseCases.Common;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Queries.GetCollectorSessions;

public sealed record GetCollectorSessionsResponse(
    IReadOnlyCollection<CollectorSessionResponse> Sessions);
