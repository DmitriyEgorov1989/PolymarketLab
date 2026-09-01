using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Ports;

/// <summary>Управляет выполняющимися в процессе WebSocket-сборщиками.</summary>
public interface ICollectorRuntime
{
    /// <summary>
    /// Необратимо запрещает запуск новых producers сессии. Уже зарегистрированный
    /// producer останавливает вызывающий lifecycle-сценарий либо его собственный observer.
    /// </summary>
    /// <param name="sessionId">Идентификатор аннулируемой сессии.</param>
    void FenceSession(CollectorSessionId sessionId);

    /// <summary>Запускает сборщик для сохранённой сессии и рынка.</summary>
    /// <param name="request">Данные сессии и рынка для запуска.</param>
    /// <param name="cancellationToken">Токен отмены ожидания запуска.</param>
    /// <returns>Успех либо ошибка запуска runtime.</returns>
    Task<UnitResult<Error>> StartAsync(
        CollectorRuntimeStartRequest request,
        CancellationToken cancellationToken);

    /// <summary>Останавливает выполняющийся сборщик указанной сессии.</summary>
    /// <param name="sessionId">Идентификатор сессии сборщика.</param>
    /// <param name="cancellationToken">Токен отмены ожидания остановки.</param>
    /// <returns>Успех либо ошибка остановки runtime.</returns>
    Task<UnitResult<Error>> StopAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken);
}
