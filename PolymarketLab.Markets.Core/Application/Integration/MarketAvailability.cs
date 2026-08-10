using PolymarketLab.Markets.Core.Ports.Dto;

namespace PolymarketLab.Markets.Core.Application.Integration;

internal static class MarketAvailability
{
    public static bool IsAvailable(ExternalMarket market, DateTimeOffset now)
    {
        return market.Active
            && !market.Closed
            && market.AcceptingOrders
            && market.OrderBookEnabled
            && IsWithinCollectionWindow(market.StartsAt, market.EndsAt, now);
    }

    public static bool IsWithinCollectionWindow(
        DateTimeOffset? startsAt,
        DateTimeOffset? endsAt,
        DateTimeOffset now)
    {
        return (startsAt is null || startsAt <= now)
            && (endsAt is null || now < endsAt);
    }
}
