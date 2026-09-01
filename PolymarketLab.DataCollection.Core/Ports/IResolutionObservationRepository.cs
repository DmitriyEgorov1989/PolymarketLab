using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports;

/// <summary>Хранит безопасные resolution observations и устойчивое состояние coordinator.</summary>
public interface IResolutionObservationRepository
{
    /// <summary>Читает состояние scanner, polling и confirmation для сессии.</summary>
    /// <param name="sessionId">Идентификатор сессии.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Сохранённое либо пустое состояние сессии.</returns>
    Task<DurableResolutionState> GetStateAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken);

    /// <summary>Атомарно сохраняет WebSocket validations и продвигает scanner cursor.</summary>
    /// <param name="scan">Проверенные результаты scan.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    Task SaveWebSocketScanAsync(
        DurableWebSocketResolutionScan scan,
        CancellationToken cancellationToken);

    /// <summary>Сохраняет безопасное наблюдение Gamma.</summary>
    /// <param name="sessionId">Идентификатор сессии.</param>
    /// <param name="observation">Проверенное наблюдение Gamma.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Идентификатор сохранённого observation.</returns>
    Task<long> SaveGammaObservationAsync(
        CollectorSessionId sessionId,
        GammaTerminalResolutionObservation observation,
        CancellationToken cancellationToken);

    /// <summary>Сохраняет безопасное наблюдение CLOB.</summary>
    /// <param name="sessionId">Идентификатор сессии.</param>
    /// <param name="observation">Проверенное наблюдение CLOB.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Идентификатор сохранённого observation.</returns>
    Task<long> SaveClobObservationAsync(
        CollectorSessionId sessionId,
        ClobTerminalResolutionObservation observation,
        CancellationToken cancellationToken);

    /// <summary>Сохраняет безопасную ошибку проверки Gamma, CLOB или WebSocket.</summary>
    /// <param name="failure">Описание ошибки без raw payload.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Идентификатор сохранённого observation.</returns>
    Task<long> SaveFailureAsync(
        DurableResolutionFailure failure,
        CancellationToken cancellationToken);

    /// <summary>Записывает время начала завершённого без overlap polling cycle.</summary>
    /// <param name="sessionId">Идентификатор сессии.</param>
    /// <param name="startedAt">Локальное UTC-время начала cycle.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    Task RecordPollingCycleAsync(
        CollectorSessionId sessionId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken);

    /// <summary>Сохраняет ссылку на пару согласованных terminal observations.</summary>
    /// <param name="sessionId">Идентификатор сессии.</param>
    /// <param name="confirmation">Ссылка на подтверждающие observations.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    Task SetConfirmationReferenceAsync(
        CollectorSessionId sessionId,
        ResolutionConfirmationReference confirmation,
        CancellationToken cancellationToken);
}
