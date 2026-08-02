using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.Errors;

public static class CollectorSessionShutdownErrors
{
    public static Error StateTransitionConflict(CollectorSessionId sessionId) => new(
        "collector.session.shutdown.state_changed",
        $"Collector session '{sessionId.Value}' remained active after repeated shutdown state updates.",
        ErrorType.Conflict);
}
