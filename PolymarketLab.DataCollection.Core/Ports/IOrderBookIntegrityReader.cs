using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports;

/// <summary>Читает сохранённые typed-события стакана для проверки целостности session snapshot.</summary>
public interface IOrderBookIntegrityReader
{
    /// <summary>Возвращает события указанной session и версии проекции в архивном порядке.</summary>
    /// <param name="sessionId">Идентификатор проверяемой collector session.</param>
    /// <param name="projectionVersion">Положительная snapshot-версия нормализации.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Сохранённые события стакана, упорядоченные по raw message и item index.</returns>
    Task<IReadOnlyList<NormalizedOrderBookEvent>> ReadAsync(
        CollectorSessionId sessionId,
        int projectionVersion,
        CancellationToken cancellationToken);
}
