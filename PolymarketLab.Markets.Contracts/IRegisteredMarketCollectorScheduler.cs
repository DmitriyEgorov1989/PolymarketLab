using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using static PolymarketLab.SharedKernel.Errors.Error;

namespace PolymarketLab.Markets.Contracts;

/// <summary>
/// Гарантирует, что явная регистрация рынка имеет долговечное задание сбора.
/// </summary>
public interface IRegisteredMarketCollectorScheduler
{
    /// <summary>
    /// Создаёт отсутствующую будущую попытку либо сохраняет идемпотентный результат.
    /// </summary>
    /// <param name="marketId">Идентификатор зарегистрированного рынка.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Успех или ошибки создания задания.</returns>
    Task<UnitResult<ErrorList>> EnsureScheduledAsync(
        MarketId marketId,
        CancellationToken cancellationToken);
}
