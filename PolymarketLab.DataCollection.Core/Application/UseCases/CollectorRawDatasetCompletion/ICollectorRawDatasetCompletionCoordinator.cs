using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorRawDatasetCompletion;

/// <summary>Завершает успешный controlled drain подтверждённой collector session.</summary>
public interface ICollectorRawDatasetCompletionCoordinator
{
    /// <summary>
    /// После durable resolution consensus останавливает producer, сохраняет весь raw tail
    /// и переводит session к ожиданию нормализации только при точном durable equality
    /// <c>received = enqueued = persisted = raw count &gt; 0</c>, прочитанном из PostgreSQL.
    /// Любая ошибка producer stop, drain, checkpoint, equality или state transition
    /// передаётся в существующий invalidation coordinator и оставляет durable diagnostic.
    /// </summary>
    /// <param name="sessionId">Идентификатор подтверждённой collector session.</param>
    /// <param name="cancellationToken">Токен отмены ожидания операции.</param>
    /// <returns>Успех либо ожидаемая ошибка orchestration или persistence.</returns>
    Task<UnitResult<Error>> CompleteAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken);
}
