using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.Markets.Core.Domain.Models.Market.Entity
{
    public sealed class MarketToken
    {
        private MarketToken()
        {
        }

        private MarketToken(
            Guid id,
            MarketId marketId,
            TokenId externalTokenId,
            string outcome,
            int outcomeIndex)
        {
            Id = id;
            MarketId = marketId;
            ExternalTokenId = externalTokenId;
            Outcome = outcome;
            OutcomeIndex = outcomeIndex;
        }

        public Guid Id { get; private set; }
        public MarketId MarketId { get; private set; } = null!;
        public TokenId ExternalTokenId { get; private set; } = null!;
        public string Outcome { get; private set; } = string.Empty;
        public int OutcomeIndex { get; private set; }

        public static Result<MarketToken, Error> Create(
            MarketId marketId,
            TokenId externalTokenId,
            string outcome,
            int outcomeIndex)
        {
            if (string.IsNullOrWhiteSpace(outcome))
                return GeneralErrors.ValueIsRequired(nameof(outcome));

            if (outcomeIndex < 0)
                return GeneralErrors.ValueIsInvalid(nameof(outcomeIndex));

            return new MarketToken(Guid.NewGuid(), marketId, externalTokenId, outcome, outcomeIndex);
        }
    }
}
