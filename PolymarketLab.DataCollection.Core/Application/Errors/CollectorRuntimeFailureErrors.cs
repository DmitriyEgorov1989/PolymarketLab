using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.Errors;

public static class CollectorRuntimeFailureErrors
{
    public static Error StateTransitionConflict(CollectorSessionId sessionId) => new(
        "collector.runtime.failure.state_changed",
        $"Collector session '{sessionId.Value}' kept changing while its runtime failure was persisted.",
        ErrorType.Conflict);
}
