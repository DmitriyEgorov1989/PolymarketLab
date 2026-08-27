using MarketAggregate = PolymarketLab.Markets.Core.Domain.Models.Market.MarketAggregate.Market;

namespace PolymarketLab.Markets.Core.Application.UseCases.Common;

/// <summary>
///     Представляет зарегистрированный рынок с раздельной identity события, рынка и точным расписанием.
/// </summary>
/// <param name="MarketId">Локальный идентификатор рынка.</param>
/// <param name="ExternalEventId">Внешний идентификатор события Gamma.</param>
/// <param name="EventSlug">Slug родительского события Gamma.</param>
/// <param name="ExternalMarketId">Внешний идентификатор дочернего рынка Gamma.</param>
/// <param name="MarketSlug">Slug дочернего рынка Gamma.</param>
/// <param name="ConditionId">Идентификатор condition в CLOB.</param>
/// <param name="Question">Вопрос рынка.</param>
/// <param name="DiscoveredAt">Неизменяемое UTC-время первого успешного обнаружения.</param>
/// <param name="ExternalCreatedAt">Gamma market <c>createdAt</c> в UTC либо <see langword="null"/>, если значение отсутствует.</param>
/// <param name="OrdersOpenedAt">Gamma market <c>acceptingOrdersTimestamp</c> в UTC либо <see langword="null"/>, если значение отсутствует.</param>
/// <param name="GammaStartDate">Gamma market <c>startDate</c> в UTC либо <see langword="null"/>, если значение отсутствует.</param>
/// <param name="EventStartsAt">Обязательное начало предметного окна в UTC.</param>
/// <param name="EventEndsAt">Обязательный конец предметного окна в UTC.</param>
/// <param name="ExternalClosedAt">Gamma market <c>closedTime</c> в UTC либо <see langword="null"/>, если значение отсутствует.</param>
/// <param name="ScheduleRefreshedAt">UTC-время последнего успешного обновления расписания.</param>
/// <param name="Tokens">Упорядоченные соответствия outcomes и tokens.</param>
public sealed record MarketResponse(
    Guid MarketId,
    string ExternalEventId,
    string EventSlug,
    string ExternalMarketId,
    string MarketSlug,
    string ConditionId,
    string Question,
    DateTimeOffset DiscoveredAt,
    DateTimeOffset? ExternalCreatedAt,
    DateTimeOffset? OrdersOpenedAt,
    DateTimeOffset? GammaStartDate,
    DateTimeOffset EventStartsAt,
    DateTimeOffset EventEndsAt,
    DateTimeOffset? ExternalClosedAt,
    DateTimeOffset ScheduleRefreshedAt,
    IReadOnlyCollection<MarketTokenResponse> Tokens)
{
    /// <summary>
    ///     Создаёт ответ из полностью материализованного aggregate рынка.
    /// </summary>
    /// <param name="market">Рынок вместе со всеми tokens.</param>
    /// <returns>Публичное представление рынка с tokens, упорядоченными по outcome index.</returns>
    public static MarketResponse FromMarket(MarketAggregate market)
    {
        return new MarketResponse(
            market.Id.Value,
            market.ExternalEventId.Value,
            market.EventSlug.Value,
            market.ExternalMarketId.Value,
            market.MarketSlug.Value,
            market.ConditionId.Value,
            market.Question,
            market.DiscoveredAt,
            market.ExternalCreatedAt,
            market.OrdersOpenedAt,
            market.GammaStartDate,
            market.EventStartsAt,
            market.EventEndsAt,
            market.ExternalClosedAt,
            market.ScheduleRefreshedAt,
            market.Tokens.OrderBy(token => token.OutcomeIndex)
                .Select(MarketTokenResponse.FromToken)
                .ToArray());
    }
}
