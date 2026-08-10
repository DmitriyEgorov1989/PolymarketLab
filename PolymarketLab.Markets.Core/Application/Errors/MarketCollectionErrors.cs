using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.Markets.Core.Application.Errors;

internal static class MarketCollectionErrors
{
    public static Error Unavailable(Guid marketId) => new(
        "market.collection.unavailable",
        $"Market '{marketId}' is not currently available for collection.",
        ErrorType.Conflict,
        "marketId");
}
