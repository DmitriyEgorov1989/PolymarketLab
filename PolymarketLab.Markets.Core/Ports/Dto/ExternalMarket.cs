namespace PolymarketLab.Markets.Core.Ports.Dto
{
    /// <summary>
    ///     Represents normalized market data returned by an external market source.
    /// </summary>
    /// <param name="ExternalMarketId">The external market identifier.</param>
    /// <param name="Slug">The child market slug, which may differ from its event slug.</param>
    /// <param name="Question">The market question.</param>
    /// <param name="ConditionId">The market condition identifier.</param>
    /// <param name="StartsAt">The external start timestamp, or <see langword="null"/> when absent.</param>
    /// <param name="EndsAt">The external end timestamp, or <see langword="null"/> when absent.</param>
    /// <param name="Active">Whether the external market is active.</param>
    /// <param name="Closed">Whether the external market is closed.</param>
    /// <param name="AcceptingOrders">Whether the external market currently accepts orders.</param>
    /// <param name="OrderBookEnabled">Whether the CLOB order book is enabled.</param>
    /// <param name="Tokens">The outcome tokens in the order supplied by the external source.</param>
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
        IReadOnlyList<ExternalMarketToken> Tokens);

    /// <summary>
    ///     Represents one ordered outcome-token mapping from an external market.
    /// </summary>
    /// <param name="Outcome">The external outcome label.</param>
    /// <param name="TokenId">The external token identifier.</param>
    /// <param name="OutcomeIndex">The zero-based position in the external outcome arrays.</param>
    public sealed record ExternalMarketToken(
        string Outcome,
        string TokenId,
        int OutcomeIndex);
}
