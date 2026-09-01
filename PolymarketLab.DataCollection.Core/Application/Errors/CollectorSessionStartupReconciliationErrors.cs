using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.Errors;

public static class CollectorSessionStartupReconciliationErrors
{
    public static Error ProcessTerminated => new(
        "collector.session.process_terminated",
        "Collector session did not complete before the previous process terminated.",
        ErrorType.Failure);

    public static Error StateTransitionConflict(CollectorSessionId sessionId) => new(
        "collector.session.reconciliation.state_changed",
        $"Collector session '{sessionId.Value}' remained active after repeated reconciliation attempts.",
        ErrorType.Conflict);
}
