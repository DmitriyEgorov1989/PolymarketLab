using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.Errors;

public static class StopCollectorErrors
{
    public static Error SessionIdRequired => new(
        "collector.stop.session_id.required",
        "Collector session id is required.",
        ErrorType.ValueIsRequired,
        "sessionId");

    public static Error SessionNotFound(Guid sessionId) => new(
        "collector.stop.session.not_found",
        $"Collector session '{sessionId}' was not found.",
        ErrorType.NotFound,
        "sessionId");

    public static Error StateTransitionConflict(CollectorSessionId sessionId) => new(
        "collector.stop.session.state_changed",
        $"Collector session '{sessionId.Value}' state changed concurrently during stop.",
        ErrorType.Conflict);
}
