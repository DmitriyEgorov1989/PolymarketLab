using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;

internal static class CollectorRuntimeErrors
{
    public static Error InvalidEndpoint(CollectorSessionId sessionId)
    {
        return new Error(
            "collector.runtime.endpoint.invalid",
            $"Collector WebSocket endpoint is invalid for session '{sessionId.Value}'.",
            ErrorType.Failure);
    }

    public static Error StartTimedOut(
        CollectorSessionId sessionId,
        TimeSpan timeout)
    {
        return new Error(
            "collector.runtime.start.timeout",
            $"Collector runtime '{sessionId.Value}' did not start within {timeout}.",
            ErrorType.Failure);
    }

    public static Error StartCancelled(CollectorSessionId sessionId)
    {
        return new Error(
            "collector.runtime.start.cancelled",
            $"Collector runtime '{sessionId.Value}' startup was cancelled.",
            ErrorType.Failure);
    }

    public static Error StartFailed(CollectorSessionId sessionId)
    {
        return new Error(
            "collector.runtime.start.failed",
            $"Collector runtime '{sessionId.Value}' failed to start.",
            ErrorType.Failure);
    }

    public static Error StopFailed(CollectorSessionId sessionId)
    {
        return new Error(
            "collector.runtime.stop.failed",
            $"Collector runtime '{sessionId.Value}' failed to stop.",
            ErrorType.Failure);
    }
}
