using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Tests.TestSupport;

internal static class CollectorSessionTestFactory
{
    public static CollectorSession CreateScheduled(
        CollectorSessionId? sessionId = null,
        MarketId? marketId = null,
        DateTimeOffset? createdAt = null)
    {
        var actualCreatedAt = createdAt ?? DateTimeOffset.Parse("2026-08-27T11:57:00Z");
        return CollectorSession.Create(
            sessionId ?? CollectorSessionId.Create(Guid.NewGuid()).Value,
            marketId ?? MarketId.Create(Guid.NewGuid()).Value,
            "event-123",
            "btc-updown-5m-1200",
            "market-123",
            "btc-updown-5m-1200",
            "0xabc",
            actualCreatedAt.AddMinutes(3),
            actualCreatedAt.AddMinutes(8),
            3,
            [
                new CollectorSessionTokenDefinition(
                    TokenId.Create("1001").Value,
                    "Yes",
                    0),
                new CollectorSessionTokenDefinition(
                    TokenId.Create("1002").Value,
                    "No",
                    1)
            ],
            actualCreatedAt).Value;
    }

    public static CollectorSession CreateStarting(
        CollectorSessionId? sessionId = null,
        MarketId? marketId = null,
        DateTimeOffset? createdAt = null)
    {
        var session = CreateScheduled(sessionId, marketId, createdAt);
        session.BeginPreparation(session.CreatedAt);
        return session;
    }

    public static CollectorSession CreateRunning(
        CollectorSessionId? sessionId = null,
        MarketId? marketId = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? subscriptionReadyAt = null)
    {
        var session = CreateStarting(sessionId, marketId, createdAt);
        MarkRunning(session, subscriptionReadyAt ?? session.CreatedAt.AddSeconds(1));
        return session;
    }

    public static void MarkRunning(
        CollectorSession session,
        DateTimeOffset subscriptionReadyAt)
    {
        if (session.Status == CollectorSessionStatus.Scheduled)
            session.BeginPreparation(subscriptionReadyAt);
        session.MarkAwaitingInitialBooks();
        session.MarkAwaitingHeartbeat();
        session.MarkRunning(subscriptionReadyAt);
    }
}
