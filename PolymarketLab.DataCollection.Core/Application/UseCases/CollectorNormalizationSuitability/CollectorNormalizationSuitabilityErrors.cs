using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorNormalizationSuitability;

/// <summary>Безопасные ожидаемые ошибки suitability gate без raw payload.</summary>
internal static class CollectorNormalizationSuitabilityErrors
{
    /// <summary>Сессия не найдена в persistence.</summary>
    public static Error SessionNotFound(CollectorSessionId sessionId) => new(
        "collector.normalization_suitability.session_not_found",
        $"Collector session '{sessionId.Value}' was not found during normalization suitability evaluation.",
        ErrorType.NotFound);

    /// <summary>У legacy session отсутствует snapshot-версия нормализации.</summary>
    public static Error ProjectionVersionMissing(CollectorSessionId sessionId) => new(
        "collector.normalization_suitability.projection_version_missing",
        $"Collector session '{sessionId.Value}' has no snapshot projection version.",
        ErrorType.Failure);

    /// <summary>У session отсутствует устойчивый момент начала ожидания нормализации.</summary>
    public static Error AwaitingNormalizationAtMissing(CollectorSessionId sessionId) => new(
        "collector.normalization_suitability.awaiting_normalization_at_missing",
        $"Collector session '{sessionId.Value}' has no normalization wait start time.",
        ErrorType.Failure);

    /// <summary>Snapshot-версия session отличается от активной runtime-версии нормализации.</summary>
    public static Error ProjectionVersionMismatch(
        CollectorSessionId sessionId,
        int snapshotVersion,
        int runtimeVersion) => new(
        "collector.normalization_suitability.projection_version_mismatch",
        $"Collector session '{sessionId.Value}' snapshot projection version '{snapshotVersion}' " +
        $"differs from the active normalization version '{runtimeVersion}'.",
        ErrorType.Conflict);

    /// <summary>Snapshot ledger содержит неподдерживаемые raw-сообщения.</summary>
    public static Error Unsupported(CollectorSessionId sessionId, long count) => new(
        "collector.normalization_suitability.unsupported",
        $"Collector session '{sessionId.Value}' snapshot ledger contains {count} unsupported raw message(s).",
        ErrorType.Failure);

    /// <summary>Snapshot ledger содержит невалидные raw-сообщения.</summary>
    public static Error Invalid(CollectorSessionId sessionId, long count) => new(
        "collector.normalization_suitability.invalid",
        $"Collector session '{sessionId.Value}' snapshot ledger contains {count} invalid raw message(s).",
        ErrorType.Failure);

    /// <summary>Snapshot ledger содержит ошибочно обработанные raw-сообщения.</summary>
    public static Error Failed(CollectorSessionId sessionId, long count) => new(
        "collector.normalization_suitability.failed",
        $"Collector session '{sessionId.Value}' snapshot ledger contains {count} failed raw message(s).",
        ErrorType.Failure);

    /// <summary>Strict WS resolution observation не указывает на обработанный snapshot item.</summary>
    public static Error ResolutionProvenanceInvalid(CollectorSessionId sessionId) => new(
        "collector.normalization_suitability.resolution_provenance_invalid",
        $"Collector session '{sessionId.Value}' strict WebSocket resolution observation " +
        "does not reference a processed snapshot market_resolved item.",
        ErrorType.Failure);

    /// <summary>Нормализация не завершилась к абсолютному deadline.</summary>
    public static Error Timeout(CollectorSessionId sessionId, DateTimeOffset deadline) => new(
        "collector.normalization_suitability.timeout",
        $"Collector session '{sessionId.Value}' normalization did not complete " +
        $"by deadline '{deadline:O}'.",
        ErrorType.Failure);

    /// <summary>Не удалось прочитать согласованный снимок пригодности.</summary>
    public static Error ReadFailed(CollectorSessionId sessionId) => new(
        "collector.normalization_suitability.read_failed",
        $"Collector session '{sessionId.Value}' normalization suitability could not be read.",
        ErrorType.Failure);

    /// <summary>Session изменилась конкурентно либо находится в недопустимом состоянии.</summary>
    public static Error StateTransitionConflict(CollectorSessionId sessionId) => new(
        "collector.normalization_suitability.state_transition_conflict",
        $"Collector session '{sessionId.Value}' changed concurrently during normalization suitability evaluation.",
        ErrorType.Conflict);
}
