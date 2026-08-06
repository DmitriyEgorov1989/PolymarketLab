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
    string? FailureMessage)
{
    public static CollectorSessionResponse FromSession(CollectorSessionAggregate session)
    {
        return new CollectorSessionResponse(
            session.Id.Value,
            session.MarketId.Value,
            session.Status.ToString(),
            session.CreatedAt,
            session.StartedAt,
            session.StoppedAt,
            session.FailureCode,
            session.FailureMessage);
    }
}
