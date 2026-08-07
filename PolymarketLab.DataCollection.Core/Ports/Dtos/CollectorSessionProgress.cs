using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports.Dtos;

public sealed record CollectorSessionProgress(
    CollectorSessionId SessionId,
    long MessagesReceived,
    long MessagesPersisted,
    DateTimeOffset? LastMessageAt,
    long ReconnectCount)
{
    public static CollectorSessionProgress Empty(CollectorSessionId sessionId) => new(
        sessionId,
        0,
        0,
        null,
        0);
}
