using PolymarketLab.DataCollection.Core.Domain.Models.Enums;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StartCollector;

public sealed record StartCollectorResponse(
    Guid SessionId,
    Guid MarketId,
    CollectorSessionStatus Status,
    bool Created);
