using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.Errors;

public static class StartCollectorErrors
{
    public static Error MarketIdRequired => new(
        "collector.start.market_id.required",
        "Market id is required.",
        ErrorType.ValueIsRequired,
        "marketId");

    public static Error MarketNotFound(Guid marketId) => new(
        "collector.start.market.not_found",
        $"Market '{marketId}' was not found.",
        ErrorType.NotFound,
        "marketId");

    public static Error TokensRequired(int count) => new(
        "collector.start.tokens.insufficient",
        $"At least two market tokens are required; found {count}.",
        ErrorType.CollectionIsTooSmall,
        "tokens");

    public static Error TokenOutcomeRequired(int outcomeIndex) => new(
        "collector.start.token.outcome.required",
        $"Outcome is required for token at index {outcomeIndex}.",
        ErrorType.ValueIsRequired,
        "tokens");

    public static Error DuplicateTokenId(string tokenId) => new(
        "collector.start.token.id.duplicate",
        $"Token id '{tokenId}' is duplicated.",
        ErrorType.Conflict,
        "tokens");

    public static Error DuplicateOutcomeIndex(int outcomeIndex) => new(
        "collector.start.token.outcome_index.duplicate",
        $"Outcome index '{outcomeIndex}' is duplicated.",
        ErrorType.Conflict,
        "tokens");

    public static Error RaceUnresolved => new(
        "collector.start.race.unresolved",
        "An active collector session conflict occurred, but the session could not be found.",
        ErrorType.Conflict);

    public static Error RuntimeStartCancelled => new(
        "collector.start.runtime.cancelled",
        "Collector runtime startup was cancelled.",
        ErrorType.Failure);
}
