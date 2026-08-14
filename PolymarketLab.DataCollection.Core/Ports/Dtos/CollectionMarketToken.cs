using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports.Dtos
{
    /// <summary>Токен исхода рынка, используемый сборщиком.</summary>
    /// <param name="TokenId">Внешний идентификатор токена.</param>
    /// <param name="Outcome">Название исхода.</param>
    /// <param name="OutcomeIndex">Позиция исхода во внешнем рынке.</param>
    public sealed record CollectionMarketToken(
        TokenId TokenId,
        string Outcome,
        int OutcomeIndex);
}
