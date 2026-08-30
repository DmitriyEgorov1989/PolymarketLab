using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Ports
{
    /// <summary>Предоставляет актуальные данные рынка, необходимые для запуска сборщика.</summary>
    public interface IMarketCollectionSource
    {
        /// <summary>Получает сохранённое окно рынка без обращения к Gamma.</summary>
        /// <param name="marketId">Идентификатор рынка.</param>
        /// <param name="cancellationToken">Токен отмены операции.</param>
        /// <returns>Сохранённое окно либо <see langword="null" />, если рынок отсутствует.</returns>
        Task<CollectionMarketWindow?> GetWindowAsync(
            MarketId marketId,
            CancellationToken cancellationToken);

        /// <summary>Получает проверенный свежим Gamma event снимок рынка по внутреннему идентификатору.</summary>
        /// <param name="marketId">Идентификатор рынка.</param>
        /// <param name="cancellationToken">Токен отмены операции.</param>
        /// <returns>Проверенный снимок, <see langword="null" /> при отсутствии либо ожидаемая ошибка.</returns>
        Task<Result<CollectionMarket?, Error>> GetByIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken);
    }
}
