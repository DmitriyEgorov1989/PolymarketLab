using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.Errors;

public static class CollectorSessionStartupReconciliationErrors
{
    public static Error StateTransitionConflict(CollectorSessionId sessionId) => new(
        "collector.session.reconciliation.state_changed",
        $"Collector session '{sessionId.Value}' remained active after repeated reconciliation attempts.",
        ErrorType.Conflict);
}
