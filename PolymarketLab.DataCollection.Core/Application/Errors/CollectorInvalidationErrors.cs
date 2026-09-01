using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.Errors;

public static class CollectorInvalidationErrors
{
    public static Error StateTransitionConflict(CollectorSessionId sessionId) => new(
        "collector.invalidation.session.state_changed",
        $"Collector session '{sessionId.Value}' changed concurrently during invalidation.",
        ErrorType.Conflict);
}
