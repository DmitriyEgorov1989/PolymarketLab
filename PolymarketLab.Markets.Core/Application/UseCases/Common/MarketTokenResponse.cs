using PolymarketLab.Markets.Core.Domain.Models.Market.Entity;

namespace PolymarketLab.Markets.Core.Application.UseCases.Common;

public sealed record MarketTokenResponse(
    string TokenId,
    string Outcome,
    int OutcomeIndex)
{
    public static MarketTokenResponse FromToken(MarketToken token)
    {
        return new MarketTokenResponse(
            token.ExternalTokenId.Value,
            token.Outcome,
            token.OutcomeIndex);
    }
}
