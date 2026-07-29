using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports.Dtos;

public sealed record RawMarketMessage(
    CollectorSessionId SessionId,
    DateTimeOffset ReceivedAt,
    byte[] Payload);
