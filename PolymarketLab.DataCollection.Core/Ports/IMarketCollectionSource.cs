using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Ports
{
    /// <summary>Предоставляет актуальные данные рынка, необходимые для запуска сборщика.</summary>
    public interface IMarketCollectionSource
    {
        /// <summary>Получает проверенный свежим Gamma event снимок рынка по внутреннему идентификатору.</summary>
        /// <param name="marketId">Идентификатор рынка.</param>
        /// <param name="cancellationToken">Токен отмены операции.</param>
        /// <returns>Проверенный снимок, <see langword="null" /> при отсутствии либо ожидаемая ошибка.</returns>
        Task<Result<CollectionMarket?, Error>> GetByIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken);
    }
}
