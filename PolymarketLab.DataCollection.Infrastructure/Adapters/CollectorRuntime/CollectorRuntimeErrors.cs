using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using System.Net.WebSockets;

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

    public static Error StopTimedOut(
        CollectorSessionId sessionId,
        TimeSpan timeout)
    {
        return new Error(
            "collector.runtime.stop.timeout",
            $"Collector runtime '{sessionId.Value}' did not stop within {timeout}.",
            ErrorType.Failure);
    }

    public static Error ReceiveFailed(CollectorSessionId sessionId)
    {
        return new Error(
            "collector.runtime.receive.failed",
            $"Collector runtime '{sessionId.Value}' failed while receiving messages.",
            ErrorType.Failure);
    }

    public static Error RemoteClosed(CollectorSessionId sessionId)
    {
        return new Error(
            "collector.runtime.receive.closed",
            $"Collector runtime '{sessionId.Value}' was closed by the remote endpoint.",
            ErrorType.Failure);
    }

    public static Error UnsupportedMessageType(
        CollectorSessionId sessionId,
        WebSocketMessageType messageType)
    {
        return new Error(
            "collector.runtime.receive.message_type.unsupported",
            $"Collector runtime '{sessionId.Value}' received unsupported message type '{messageType}'.",
            ErrorType.Failure);
    }

    public static Error MessageTooLarge(
        CollectorSessionId sessionId,
        int maximumMessageSize)
    {
        return new Error(
            "collector.runtime.receive.message_too_large",
            $"Collector runtime '{sessionId.Value}' received a message larger than {maximumMessageSize} bytes.",
            ErrorType.Failure);
    }

    public static Error IngestionClosed(CollectorSessionId sessionId)
    {
        return new Error(
            "collector.runtime.ingestion.closed",
            $"Raw message ingestion is closed for collector runtime '{sessionId.Value}'.",
            ErrorType.Failure);
    }

    public static Error EnqueueCancelled(CollectorSessionId sessionId)
    {
        return new Error(
            "collector.runtime.ingestion.enqueue_cancelled",
            $"Raw message enqueue was cancelled for collector runtime '{sessionId.Value}' after a complete message was received.",
            ErrorType.Failure);
    }

    public static Error RuntimeStopping(CollectorSessionId sessionId)
    {
        return new Error(
            "collector.runtime.stopping",
            $"Collector runtime is stopping and cannot start session '{sessionId.Value}'.",
            ErrorType.Failure);
    }

    public static Error SessionInvalidating(CollectorSessionId sessionId)
    {
        return new Error(
            "collector.runtime.session.invalidating",
            $"Collector runtime cannot start invalidating session '{sessionId.Value}'.",
            ErrorType.Failure);
    }

    public static Error ReadinessPersistenceFailed(CollectorSessionId sessionId)
    {
        return new Error(
            "collector.runtime.readiness.persistence_failed",
            $"Collector runtime readiness update failed for session '{sessionId.Value}'.",
            ErrorType.Failure);
    }

    public static Error ReadinessTimedOut(CollectorSessionId sessionId)
    {
        return new Error(
            "collector.runtime.readiness.timeout",
            $"Collector runtime '{sessionId.Value}' did not become ready before the readiness deadline.",
            ErrorType.Failure);
    }

    public static Error HeartbeatTimedOut(
        CollectorSessionId sessionId,
        TimeSpan timeout)
    {
        return new Error(
            "collector.runtime.heartbeat.timeout",
            $"Collector runtime '{sessionId.Value}' did not receive PONG within {timeout}.",
            ErrorType.Failure);
    }

    public static Error ProtocolViolation(CollectorSessionId sessionId)
    {
        return new Error(
            "collector.runtime.protocol.violation",
            $"Collector runtime '{sessionId.Value}' observed a protocol or identity violation.",
            ErrorType.Failure);
    }
}
