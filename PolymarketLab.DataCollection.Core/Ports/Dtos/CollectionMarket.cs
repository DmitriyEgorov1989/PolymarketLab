using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports.Dtos
{
    public sealed record CollectionMarket(
        MarketId MarketId,
        string Slug,
        IReadOnlyCollection<CollectionMarketToken> Tokens);
}
