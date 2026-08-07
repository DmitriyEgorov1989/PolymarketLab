using PolymarketLab.DataCollection.Core.Ports.Dtos;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Common;

public sealed record CollectorSessionResponse(
    Guid SessionId,
    Guid MarketId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? StoppedAt,
    string? FailureCode,
    string? FailureMessage,
    long MessagesReceived,
    long MessagesPersisted,
    DateTimeOffset? LastMessageAt,
    long ReconnectCount)
{
    public static CollectorSessionResponse FromSession(
        CollectorSessionAggregate session,
        CollectorSessionProgress progress)
    {
        return new CollectorSessionResponse(
            session.Id.Value,
            session.MarketId.Value,
            session.Status.ToString(),
            session.CreatedAt,
            session.StartedAt,
            session.StoppedAt,
            session.FailureCode,
            session.FailureMessage,
            progress.MessagesReceived,
            progress.MessagesPersisted,
            progress.LastMessageAt,
            progress.ReconnectCount);
    }
}
