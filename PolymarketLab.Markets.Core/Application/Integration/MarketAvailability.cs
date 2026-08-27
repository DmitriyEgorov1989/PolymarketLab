using PolymarketLab.Markets.Core.Ports.Dto;

namespace PolymarketLab.Markets.Core.Application.Integration;

internal static class MarketAvailability
{
    public static bool IsAvailable(ExternalMarket market)
    {
        return market.Active
            && !market.Closed
            && market.AcceptingOrders
            && market.OrderBookEnabled;
    }

    public static bool IsTerminal(ExternalMarket market)
    {
        return market.Closed
            || market.ExternalClosedAt is not null
            || string.Equals(market.UmaResolutionStatus, "resolved", StringComparison.OrdinalIgnoreCase);
    }
}
