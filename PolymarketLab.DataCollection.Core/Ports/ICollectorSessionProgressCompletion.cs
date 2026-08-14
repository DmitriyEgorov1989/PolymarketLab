using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Ports;

/// <summary>Ожидает сохранения всех поставленных в очередь сообщений сессии.</summary>
public interface ICollectorSessionProgressCompletion
{
    /// <summary>Ожидает durable persistence хвоста сообщений и выполняет финальный checkpoint.</summary>
    /// <param name="sessionId">Идентификатор завершаемой сессии.</param>
    /// <param name="cancellationToken">Токен отмены ожидания.</param>
    /// <returns>Успех либо ошибка завершения persistence.</returns>
    Task<UnitResult<Error>> CompleteAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken);
}
