using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.Errors;

public static class CollectorSchedulingErrors
{
    public static Error StateTransitionConflict(CollectorSessionId sessionId) => new(
        "collector.scheduler.session.state_changed",
        $"Collector session '{sessionId.Value}' changed concurrently during scheduling.",
        ErrorType.Conflict);
}
