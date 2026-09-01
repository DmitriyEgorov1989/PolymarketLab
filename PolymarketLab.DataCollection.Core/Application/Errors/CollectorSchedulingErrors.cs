using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.Errors;

public static class CollectorSchedulingErrors
{
    public static Error SessionInvalid => new(
        "collector.scheduler.session.invalid",
        "Collector session no longer satisfies its immutable schedule and readiness boundaries.",
        ErrorType.Failure);

    public static Error RuntimeStartFailed => new(
        "collector.scheduler.runtime.start_failed",
        "Collector runtime startup failed before the session became ready.",
        ErrorType.Failure);

    public static Error StateTransitionConflict(CollectorSessionId sessionId) => new(
        "collector.scheduler.session.state_changed",
        $"Collector session '{sessionId.Value}' changed concurrently during scheduling.",
        ErrorType.Conflict);
}
