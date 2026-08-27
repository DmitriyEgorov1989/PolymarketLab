using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Ports;

/// <summary>Получает проверенное состояние разрешения рынка из Gamma.</summary>
public interface IGammaTerminalResolutionSource
{
    /// <summary>Проверяет текущее состояние Gamma относительно ожидаемой identity рынка.</summary>
    /// <param name="request">Ожидаемая identity рынка из неизменяемого снимка сессии.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Безопасное наблюдение Gamma либо диагностированная ошибка интеграции.</returns>
    Task<Result<GammaTerminalResolutionObservation, Error>> GetAsync(
        GammaTerminalResolutionRequest request,
        CancellationToken cancellationToken);
}
