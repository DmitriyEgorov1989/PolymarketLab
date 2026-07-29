using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;

/// <summary>
/// Управляет WebSocket-соединением одной сессии сбора данных.
/// </summary>
internal interface ICollectorWorker
{
    /// <summary>
    /// Возвращает задачу полного завершения обработчика вместе с результатом,
    /// причиной завершения и временем её обнаружения.
    /// </summary>
    Task<CollectorWorkerCompletion> Completion { get; }

    /// <summary>
    /// Устанавливает соединение, отправляет сообщение подписки и запускает получение данных.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены запуска.</param>
    Task<UnitResult<Error>> StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Запрашивает остановку, ожидает завершения текущих операций и закрывает соединение.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены ожидания остановки.</param>
    Task<UnitResult<Error>> StopAsync(CancellationToken cancellationToken);
}
