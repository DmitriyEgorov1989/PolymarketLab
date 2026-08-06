using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.Errors;

internal static class CollectorSessionQueryErrors
{
    public static Error SessionIdRequired => new(
        "collector.query.session_id.required",
        "Collector session id is required.",
        ErrorType.ValueIsRequired,
        "sessionId");

    public static Error MarketIdRequired => new(
        "collector.query.market_id.required",
        "Market id is required.",
        ErrorType.ValueIsRequired,
        "marketId");

    public static Error NotFound(Guid sessionId) => new(
        "collector.query.session.not_found",
        $"Collector session '{sessionId}' was not found.",
        ErrorType.NotFound,
        "sessionId");
}
