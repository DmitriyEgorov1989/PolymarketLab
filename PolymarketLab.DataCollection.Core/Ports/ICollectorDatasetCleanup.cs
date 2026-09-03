using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.Errors;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Ports;

/// <summary>Атомарно удаляет перестраиваемые данные аннулируемой сессии и завершает её как ошибочную.</summary>
public interface ICollectorDatasetCleanup
{
    /// <summary>
    /// Удаляет dataset сессии, сохраняет audit и выполняет переход <c>Invalidating -&gt; Failed</c>.
    /// </summary>
    /// <param name="session">Аннулируемая сессия; после успеха имеет статус <c>Failed</c>.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Сохранённый audit либо ожидаемая ошибка состояния.</returns>
    Task<Result<CollectorDatasetCleanupAudit, Error>> CleanupAsync(
        CollectorSessionAggregate session,
        CancellationToken cancellationToken);
}
