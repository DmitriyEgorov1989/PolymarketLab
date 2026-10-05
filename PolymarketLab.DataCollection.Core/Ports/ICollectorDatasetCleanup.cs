using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.Errors;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Ports;

/// <summary>Атомарно удаляет перестраиваемые данные failed-сессии и подтверждает их удаление.</summary>
public interface ICollectorDatasetCleanup
{
    /// <summary>
    /// Удаляет dataset, сохраняет audit и завершает invalidation либо истёкшее диагностическое хранение.
    /// </summary>
    /// <param name="session">
    /// Сессия в состоянии <c>Invalidating</c> либо <c>Failed/Retained</c> с истёкшим сроком;
    /// после успеха имеет disposition <c>Deleted</c>.
    /// </param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Сохранённый audit либо ожидаемая ошибка состояния.</returns>
    Task<Result<CollectorDatasetCleanupAudit, Error>> CleanupAsync(
        CollectorSessionAggregate session,
        CancellationToken cancellationToken);
}
