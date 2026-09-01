using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionInvalidation;

/// <summary>Необратимо устанавливает durable write fence неполной collector session.</summary>
public interface ICollectorSessionInvalidationCoordinator
{
    /// <summary>Запрещает новые producers и атомарно сохраняет первую причину invalidation.</summary>
    /// <param name="sessionId">Идентификатор аннулируемой сессии.</param>
    /// <param name="occurredAt">Момент события, сделавшего dataset непригодным.</param>
    /// <param name="reason">Категория остановки сессии.</param>
    /// <param name="failure">Безопасная исходная diagnostic без raw payload.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>
    /// Актуальная сессия; <see langword="null" />, если сессия не найдена,
    /// либо исходная persistence error.
    /// </returns>
    Task<Result<CollectorSessionAggregate?, Error>> InvalidateAsync(
        CollectorSessionId sessionId,
        DateTimeOffset occurredAt,
        CollectorStopReason reason,
        Error failure,
        CancellationToken cancellationToken);
}
