using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorScheduling;

/// <summary>Продвигает сохранённые collector sessions по временным границам подготовки.</summary>
public interface ICollectorScheduler
{
    /// <summary>Обрабатывает сохранённую session с уже полученным свежим снимком рынка.</summary>
    /// <param name="session">Сохранённая session.</param>
    /// <param name="market">Свежий проверенный Gamma snapshot.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Фактическое сохранённое состояние либо ожидаемая ошибка.</returns>
    Task<Result<CollectorSessionAggregate, Error>> PrepareAsync(
        CollectorSessionAggregate session,
        CollectionMarket market,
        CancellationToken cancellationToken);

    /// <summary>Возвращает идентификаторы активных sessions для независимой обработки.</summary>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Снимок идентификаторов активных sessions.</returns>
    Task<IReadOnlyCollection<CollectorSessionId>> GetActiveSessionIdsAsync(
        CancellationToken cancellationToken);

    /// <summary>Обрабатывает одну сохранённую session в принадлежащей ей области зависимостей.</summary>
    /// <param name="sessionId">Идентификатор session.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Успех либо ожидаемая ошибка session.</returns>
    Task<UnitResult<Error>> TickSessionAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken);

    /// <summary>Обрабатывает все активные sessions, если наступили их границы.</summary>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Успех либо ожидаемая ошибка tick.</returns>
    Task<UnitResult<Error>> TickAsync(CancellationToken cancellationToken);
}
