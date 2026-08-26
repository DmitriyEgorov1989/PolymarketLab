using CSharpFunctionalExtensions;
using PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects;
using PolymarketLab.Markets.Core.Ports.Dto;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.Markets.Core.Ports
{
    /// <summary>
    ///     Порт для получения данных рынка из внешнего источника.
    ///     Скрывает от Core детали HTTP, формат ответа и конкретный API Polymarket.
    /// </summary>
    public interface IExternalMarketGateway
    {
        /// <summary>
        ///     Gets an event and its single supported child market by the Polymarket event slug.
        /// </summary>
        /// <param name="eventSlug">The validated event slug.</param>
        /// <param name="cancellationToken">The cancellation token for the external request.</param>
        /// <returns>The event discovery result, or an expected external-source error.</returns>
        Task<Result<ExternalEvent, Error>> GetByEventSlugAsync(
            EventSlug eventSlug,
            CancellationToken cancellationToken);

        /// <summary>
        ///     Получает рынок по его Polymarket slug и преобразует внешний ответ
        ///     в нормализованную модель <see cref="ExternalMarket"/>.
        /// </summary>
        /// <param name="slug">Проверенный slug рынка.</param>
        /// <param name="cancellationToken">Токен отмены внешнего запроса.</param>
        /// <returns>Данные рынка или ожидаемая ошибка внешнего источника.</returns>
        Task<Result<ExternalMarket, Error>> GetByMarketSlugAsync(
            MarketSlug slug,
            CancellationToken cancellationToken);
    }
}
