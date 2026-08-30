using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
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

    /// <summary>Обрабатывает текущую global exclusive session, если наступила её граница.</summary>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Успех либо ожидаемая ошибка tick.</returns>
    Task<UnitResult<Error>> TickAsync(CancellationToken cancellationToken);
}
