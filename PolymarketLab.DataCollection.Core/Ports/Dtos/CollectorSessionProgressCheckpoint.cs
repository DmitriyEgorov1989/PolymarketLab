using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports.Dtos;

public sealed record CollectorSessionProgressCheckpoint(
    CollectorSessionId SessionId,
    long MessagesReceived,
    DateTimeOffset? LastMessageAt,
    long ReconnectCount);
