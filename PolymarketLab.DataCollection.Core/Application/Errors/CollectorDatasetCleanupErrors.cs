using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.Errors;

/// <summary>Содержит ожидаемые ошибки атомарной очистки dataset.</summary>
public static class CollectorDatasetCleanupErrors
{
    /// <summary>Создаёт ошибку для отсутствующей сессии.</summary>
    public static Error SessionNotFound(CollectorSessionId sessionId) => new(
        "collector.dataset_cleanup.session.not_found",
        $"Collector session '{sessionId.Value}' was not found during dataset cleanup.",
        ErrorType.NotFound);

    /// <summary>Создаёт ошибку для сессии, которую нельзя безопасно очищать.</summary>
    public static Error InvalidStatus(
        CollectorSessionId sessionId,
        CollectorSessionStatus status) => new(
        "collector.dataset_cleanup.session.invalid_status",
        $"Collector session '{sessionId.Value}' has status '{status}' and cannot be cleaned.",
        ErrorType.Conflict);

    /// <summary>Создаёт ошибку при нарушении атомарного перехода в конечное состояние.</summary>
    public static Error StateTransitionConflict(CollectorSessionId sessionId) => new(
        "collector.dataset_cleanup.session.state_changed",
        $"Collector session '{sessionId.Value}' changed concurrently during dataset cleanup.",
        ErrorType.Conflict);
}
