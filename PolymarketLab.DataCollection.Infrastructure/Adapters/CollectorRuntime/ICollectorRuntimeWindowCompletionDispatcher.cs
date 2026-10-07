using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;

/// <summary>
/// Передаёт штатное завершение окна сбора в session-scoped lifecycle coordinator.
/// </summary>
internal interface ICollectorRuntimeWindowCompletionDispatcher
{
    /// <summary>
    /// Запускает controlled drain и последующую проверку набора данных указанной сессии.
    /// </summary>
    /// <param name="sessionId">Идентификатор завершившей окно collector session.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    Task DispatchAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken);
}
