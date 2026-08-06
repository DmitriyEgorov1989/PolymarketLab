using PolymarketLab.DataCollection.Core.Application.UseCases.Common;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StopCollector;

public sealed record StopCollectorResponse(
    CollectorSessionResponse Session);
