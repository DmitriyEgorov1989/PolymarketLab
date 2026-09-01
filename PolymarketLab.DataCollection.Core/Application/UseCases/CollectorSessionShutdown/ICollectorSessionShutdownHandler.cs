using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionShutdown;

/// <summary>Сохраняет invalidation fence для сессий, затронутых остановкой приложения.</summary>
public interface ICollectorSessionShutdownHandler
{
    /// <summary>Начинает инвалидацию сессии до остановки её runtime.</summary>
    /// <param name="sessionId">Идентификатор останавливаемой сессии.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Успех либо исходная ошибка чтения или сохранения сессии.</returns>
    Task<UnitResult<Error>> MarkStoppingAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken);

    /// <summary>Идемпотентно подтверждает, что invalidation fence уже сохранён.</summary>
    /// <param name="sessionId">Идентификатор остановленной сессии.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Успех либо исходная ошибка чтения или сохранения сессии.</returns>
    Task<UnitResult<Error>> MarkStoppedAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken);

    /// <summary>Сохраняет первую ошибку остановки как причину инвалидации.</summary>
    /// <param name="sessionId">Идентификатор сессии.</param>
    /// <param name="error">Исходная ошибка без raw payload.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Успех либо исходная ошибка чтения или сохранения сессии.</returns>
    Task<UnitResult<Error>> MarkFailedAsync(
        CollectorSessionId sessionId,
        Error error,
        CancellationToken cancellationToken);
}
