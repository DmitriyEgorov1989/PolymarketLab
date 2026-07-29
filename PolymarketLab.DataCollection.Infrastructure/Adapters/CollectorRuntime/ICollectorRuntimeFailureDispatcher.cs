using PolymarketLab.DataCollection.Core.Ports.Dtos;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;

/// <summary>
/// Передаёт сведения об автономной ошибке сборщика обработчику,
/// который сохраняет неуспешное состояние сессии сбора данных.
/// </summary>
internal interface ICollectorRuntimeFailureDispatcher
{
    /// <summary>Передаёт сведения об ошибке для сохранения в отдельной области зависимостей.</summary>
    /// <param name="failure">Идентификатор сессии, время и причина завершения.</param>
    /// <param name="cancellationToken">Токен отмены ожидания сохранения.</param>
    Task DispatchAsync(
        CollectorRuntimeFailure failure,
        CancellationToken cancellationToken);
}
