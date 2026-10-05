using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorFailedDatasetFinalization;

/// <summary>Содержит ожидаемые ошибки финализации failed dataset.</summary>
public static class CollectorFailedDatasetFinalizationErrors
{
    /// <summary>Создаёт ошибку конкурентного изменения сессии до несовместимого состояния.</summary>
    /// <param name="sessionId">Идентификатор сессии.</param>
    /// <returns>Ошибка конфликта состояния.</returns>
    public static Error StateTransitionConflict(CollectorSessionId sessionId) => new(
        "collector.failed_dataset_finalization.session.state_changed",
        $"Collector session '{sessionId.Value}' changed concurrently during failed dataset finalization.",
        ErrorType.Conflict);
}
