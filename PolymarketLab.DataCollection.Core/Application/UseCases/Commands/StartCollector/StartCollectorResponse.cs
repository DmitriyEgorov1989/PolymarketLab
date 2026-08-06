namespace PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StartCollector;

public sealed record StartCollectorResponse(
    Guid SessionId,
    Guid MarketId,
    string Status);
