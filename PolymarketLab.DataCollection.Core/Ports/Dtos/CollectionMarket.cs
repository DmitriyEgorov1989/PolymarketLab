using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports.Dtos
{
    /// <summary>Минимальные данные рынка для запуска сбора.</summary>
    /// <param name="MarketId">Внутренний идентификатор рынка.</param>
    /// <param name="Slug">Slug рынка Polymarket.</param>
    /// <param name="Tokens">Токены исходов для WebSocket-подписки.</param>
    public sealed record CollectionMarket(
        MarketId MarketId,
        string Slug,
        IReadOnlyCollection<CollectionMarketToken> Tokens);
}
