using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.Errors;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorFailedDatasetFinalization;

/// <summary>Финализирует набор данных неуспешной сессии согласно сохранённой политике.</summary>
public interface ICollectorFailedDatasetFinalizer
{
    /// <summary>Завершает invalidation либо удаляет диагностический набор после срока хранения.</summary>
    /// <param name="session">Аннулируемая или сохранённая failed-сессия.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Успех, включая идемпотентный результат, либо исходная ошибка persistence или cleanup.</returns>
    Task<UnitResult<Error>> FinalizeAsync(
        CollectorSessionAggregate session,
        CancellationToken cancellationToken);
}
