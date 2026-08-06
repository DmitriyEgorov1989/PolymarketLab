using MarketAggregate = PolymarketLab.Markets.Core.Domain.Models.Market.MarketAggregate.Market;

namespace PolymarketLab.Markets.Core.Application.UseCases.Common;

public sealed record MarketResponse(
    Guid MarketId,
    string ExternalMarketId,
    string Slug,
    string ConditionId,
    string Question,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    IReadOnlyCollection<MarketTokenResponse> Tokens)
{
    public static MarketResponse FromMarket(MarketAggregate market)
    {
        return new MarketResponse(
            market.Id.Value,
            market.ExternalId.Value,
            market.Slug.Value,
            market.ConditionId.Value,
            market.Question,
            market.StartsAt,
            market.EndsAt,
            market.Tokens
                .Select(MarketTokenResponse.FromToken)
                .ToArray());
    }
}
