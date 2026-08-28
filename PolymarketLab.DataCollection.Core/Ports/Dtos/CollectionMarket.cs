using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports.Dtos
{
    /// <summary>Проверенный снимок рынка для запуска сбора.</summary>
    /// <param name="MarketId">Внутренний идентификатор рынка.</param>
    /// <param name="ExternalEventId">Идентификатор родительского события Gamma.</param>
    /// <param name="EventSlug">Slug родительского события Gamma.</param>
    /// <param name="ExternalMarketId">Идентификатор дочернего рынка Gamma.</param>
    /// <param name="MarketSlug">Slug дочернего рынка Gamma.</param>
    /// <param name="ConditionId">Идентификатор condition рынка.</param>
    /// <param name="EventStartsAt">Точное UTC-время начала предметного окна.</param>
    /// <param name="EventEndsAt">Точное UTC-время окончания предметного окна.</param>
    /// <param name="Active">Актуальный признак активности Gamma.</param>
    /// <param name="Closed">Актуальный признак закрытия Gamma.</param>
    /// <param name="AcceptingOrders">Актуальный признак приёма заявок Gamma.</param>
    /// <param name="OrderBookEnabled">Актуальный признак включённого CLOB order book.</param>
    /// <param name="Tokens">Токены исходов в порядке Gamma.</param>
    public sealed record CollectionMarket(
        MarketId MarketId,
        string ExternalEventId,
        string EventSlug,
        string ExternalMarketId,
        string MarketSlug,
        string ConditionId,
        DateTimeOffset EventStartsAt,
        DateTimeOffset EventEndsAt,
        bool Active,
        bool Closed,
        bool AcceptingOrders,
        bool OrderBookEnabled,
        IReadOnlyList<CollectionMarketToken> Tokens);
}
