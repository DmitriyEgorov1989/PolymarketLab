using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Application.Simulation.Datasets;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Ports;

/// <summary>Читает согласованный снимок одной сохранённой сессии без изменения архива.</summary>
public interface IExperimentDatasetReader
{
    /// <summary>Получает манифест, данные и независимый отчёт пригодности.</summary>
    /// <param name="request">Точный выбор сессии, версии и закреплённых входных данных.</param>
    /// <param name="cancellationToken">Токен отмены чтения.</param>
    /// <returns>Снимок либо ошибка выбора, чтения или несовпадения закреплённого входа.</returns>
    Task<Result<ExperimentDatasetSnapshot, Error>> ReadAsync(
        ExperimentDatasetRequest request,
        CancellationToken cancellationToken);
}
