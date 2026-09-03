using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Ports;

/// <summary>Ожидает сохранения всех поставленных в очередь сообщений сессии.</summary>
public interface ICollectorSessionProgressCompletion
{
    /// <summary>
    /// Ждёт, пока durable persisted counter достигнет final enqueued boundary,
    /// и сохраняет final monotonic checkpoint. Порт не принимает business-решение
    /// о пригодности dataset: точное равенство counters и raw rows проверяет
    /// application coordinator отдельным PostgreSQL read после этого вызова.
    /// </summary>
    /// <param name="sessionId">Идентификатор завершаемой сессии.</param>
    /// <param name="cancellationToken">Токен отмены ожидания.</param>
    /// <returns>Успех либо ошибка завершения persistence.</returns>
    Task<UnitResult<Error>> CompleteAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken);
}
