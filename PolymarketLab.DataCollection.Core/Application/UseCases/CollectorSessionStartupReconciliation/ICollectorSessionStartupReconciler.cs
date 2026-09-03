using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionStartupReconciliation;

/// <summary>
/// Согласует сохранённые сессии сбора данных с состоянием нового процесса приложения.
/// Незавершённые сессии предыдущего процесса аннулируются, их dataset очищается,
/// а collection не возобновляется.
/// </summary>
public interface ICollectorSessionStartupReconciler
{
    /// <summary>Выполняет согласование до начала обработки входящих запросов.</summary>
    /// <param name="cancellationToken">Токен отмены запуска приложения.</param>
    /// <returns>Результат согласования всех найденных активных сессий.</returns>
    Task<UnitResult<Error>> ReconcileAsync(CancellationToken cancellationToken);
}
