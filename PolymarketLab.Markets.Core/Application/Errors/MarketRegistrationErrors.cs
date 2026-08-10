using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.Markets.Core.Application.Errors;

internal static class MarketRegistrationErrors
{
    public static Error IdentityConflict => new(
        "market.registration.identity_conflict",
        "Market identifiers belong to different registered markets.",
        ErrorType.Conflict);

    public static Error OrderBookDisabled => new(
        "market.registration.order_book_disabled",
        "The market order book is disabled.",
        ErrorType.Conflict);

    public static Error Unavailable => new(
        "market.registration.unavailable",
        "The market is not currently available for collection.",
        ErrorType.Conflict);

    public static Error TokensRequired => new(
        "market.registration.tokens_required",
        "The market must contain at least one token.",
        ErrorType.ValueIsRequired,
        "Tokens");

    public static Error SlugMismatch(string requestedSlug, string externalSlug) => new(
        "market.registration.slug_mismatch",
        $"Requested slug '{requestedSlug}' does not match external slug '{externalSlug}'.",
        ErrorType.Conflict,
        "Slug");

    public static Error RaceUnresolved => new(
        "market.registration.race_unresolved",
        "A concurrent market registration could not be resolved.",
        ErrorType.Conflict);
}
