namespace PolymarketLab.Markets.Core.Ports.Dto
{
    public sealed record ExternalMarket(
        string ExternalMarketId,
        string Slug,
        string Question,
        string ConditionId,
        DateTimeOffset? StartsAt,
        DateTimeOffset? EndsAt,
        bool Active,
        bool Closed,
        bool AcceptingOrders,
        bool OrderBookEnabled,
        IReadOnlyCollection<ExternalMarketToken> Tokens);

    public sealed record ExternalMarketToken(
        string Outcome,
        string TokenId,
        int OutcomeIndex);
}
