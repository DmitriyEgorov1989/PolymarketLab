using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorRawDatasetCompletion;

/// <summary>Безопасные ожидаемые ошибки завершения raw dataset без raw payload.</summary>
internal static class CollectorRawDatasetCompletionErrors
{
    /// <summary>Сессия не найдена в persistence.</summary>
    public static Error SessionNotFound(CollectorSessionId sessionId) => new(
        "collector.raw_completion.session_not_found",
        $"Collector session '{sessionId.Value}' was not found during raw completion.",
        ErrorType.NotFound);

    /// <summary>У сессии нет durable confirmation resolution.</summary>
    public static Error ResolutionNotConfirmed(CollectorSessionId sessionId) => new(
        "collector.raw_completion.resolution_not_confirmed",
        $"Collector session '{sessionId.Value}' has no durable resolution confirmation.",
        ErrorType.Conflict);

    /// <summary>Durable counters и авторитетное количество raw rows не совпадают.</summary>
    public static Error AccountingMismatch(CollectorSessionProgress progress) => new(
        "collector.raw_completion.accounting_mismatch",
        $"Collector session '{progress.SessionId.Value}' raw accounting differs: " +
        $"received={progress.MessagesReceived}, enqueued={progress.MessagesEnqueued}, " +
        $"persisted={progress.MessagesPersisted}, raw={progress.RawMessageCount}.",
        ErrorType.Failure);

    /// <summary>Не удалось прочитать финальный durable snapshot прогресса.</summary>
    public static Error ProgressReadFailed(CollectorSessionId sessionId) => new(
        "collector.raw_completion.progress_read_failed",
        $"Collector session '{sessionId.Value}' final raw accounting could not be read.",
        ErrorType.Failure);

    /// <summary>Session изменилась конкурентно либо находится в недопустимом состоянии.</summary>
    public static Error StateTransitionConflict(CollectorSessionId sessionId) => new(
        "collector.raw_completion.state_transition_conflict",
        $"Collector session '{sessionId.Value}' changed concurrently during raw completion.",
        ErrorType.Conflict);
}
