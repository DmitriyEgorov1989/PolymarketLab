using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports.Dtos
{
    public sealed record CollectionMarketToken(
        TokenId TokenId,
        string Outcome,
        int OutcomeIndex);
}
