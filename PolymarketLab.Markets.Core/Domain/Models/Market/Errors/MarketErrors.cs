using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.Markets.Core.Domain.Models.Market.Errors;

internal static class MarketErrors
{
    public static Error DuplicateTokenId(string tokenId) => new(
        "market.token.external_id.duplicate",
        $"Token ID '{tokenId}' is already registered for the market.",
        ErrorType.Conflict,
        "externalTokenId");

    public static Error DuplicateOutcomeIndex(int outcomeIndex) => new(
        "market.token.outcome_index.duplicate",
        $"Outcome index '{outcomeIndex}' is already registered for the market.",
        ErrorType.Conflict,
        "outcomeIndex");
}
