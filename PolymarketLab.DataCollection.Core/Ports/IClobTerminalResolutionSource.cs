using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Ports;

/// <summary>Получает проверенное состояние разрешения рынка из CLOB.</summary>
public interface IClobTerminalResolutionSource
{
    /// <summary>Проверяет текущее состояние CLOB относительно ожидаемой identity рынка.</summary>
    /// <param name="request">Ожидаемая identity рынка из неизменяемого снимка сессии.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Безопасное наблюдение CLOB либо диагностированная ошибка интеграции.</returns>
    Task<Result<ClobTerminalResolutionObservation, Error>> GetAsync(
        ClobTerminalResolutionRequest request,
        CancellationToken cancellationToken);
}
