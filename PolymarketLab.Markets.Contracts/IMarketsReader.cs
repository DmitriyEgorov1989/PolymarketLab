using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.Markets.Contracts;

/// <summary>Предоставляет проверенные данные зарегистрированных рынков другим модулям.</summary>
public interface IMarketsReader
{
    /// <summary>Получает сохранённое окно рынка без обращения к Gamma.</summary>
    /// <param name="marketId">Внутренний идентификатор зарегистрированного рынка.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Сохранённое окно либо <see langword="null"/>, если рынок отсутствует.</returns>
    Task<MarketCollectionWindow?> GetCollectionWindowAsync(
        MarketId marketId,
        CancellationToken cancellationToken);

    /// <summary>Получает проверенный свежим Gamma event снимок рынка для collection session.</summary>
    /// <param name="marketId">Внутренний идентификатор зарегистрированного рынка.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Проверенный снимок, <see langword="null"/> при отсутствии рынка либо ожидаемая ошибка.</returns>
    Task<Result<MarketForCollection?, Error>> GetForCollectionAsync(
        MarketId marketId,
        CancellationToken cancellationToken);
}
