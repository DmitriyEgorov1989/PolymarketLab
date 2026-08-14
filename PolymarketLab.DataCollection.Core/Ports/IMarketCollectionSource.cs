using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Ports
{
    /// <summary>Предоставляет актуальные данные рынка, необходимые для запуска сборщика.</summary>
    public interface IMarketCollectionSource
    {
        /// <summary>Получает доступный для сбора рынок по внутреннему идентификатору.</summary>
        /// <param name="marketId">Идентификатор рынка.</param>
        /// <param name="cancellationToken">Токен отмены операции.</param>
        /// <returns>Рынок, <see langword="null" /> при отсутствии либо ожидаемая ошибка интеграции.</returns>
        Task<Result<CollectionMarket?, Error>> GetByIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken);
    }
}
