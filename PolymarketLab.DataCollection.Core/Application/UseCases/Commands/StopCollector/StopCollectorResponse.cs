using PolymarketLab.DataCollection.Core.Domain.Models.Enums;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StopCollector;

public sealed record StopCollectorResponse(
    Guid SessionId,
    Guid MarketId,
    CollectorSessionStatus Status,
    bool Stopped);
