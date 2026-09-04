using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Common;

/// <summary>Строит единый безопасный HTTP-снимок сессии из существующих read slices.</summary>
public interface ICollectorSessionResponseFactory
{
    /// <summary>Создаёт полный HTTP-ответ сессии.</summary>
    /// <param name="session">Загруженный агрегат сессии.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Полный безопасный снимок lifecycle и evidence сессии.</returns>
    Task<CollectorSessionResponse> CreateAsync(
        CollectorSessionAggregate session,
        CancellationToken cancellationToken);
}
