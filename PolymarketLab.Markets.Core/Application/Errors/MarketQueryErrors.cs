using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.Markets.Core.Application.Errors;

internal static class MarketQueryErrors
{
    public static Error MarketIdRequired => new(
        "market.query.market_id.required",
        "Market id is required.",
        ErrorType.ValueIsRequired,
        "marketId");

    public static Error NotFound(Guid marketId) => new(
        "market.query.not_found",
        $"Market '{marketId}' was not found.",
        ErrorType.NotFound,
        "marketId");
}
