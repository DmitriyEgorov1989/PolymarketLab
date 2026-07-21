using CSharpFunctionalExtensions;
using PolymarketLab.Markets.Core.Domain.Models.Market.Entity;
using PolymarketLab.Markets.Core.Domain.Models.Market.Errors;
using PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects;
using PolymarketLab.SharedKernel.DomainModels;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.Markets.Core.Domain.Models.Market.MarketAggregate
{
    public sealed class Market : Aggregate<MarketId>
    {
        private readonly List<MarketToken> _tokens = [];


        private Market()
        {
        }

        private Market(
            MarketId id,
            ExternalMarketId externalId,
            MarketSlug slug,
            ConditionId conditionId,
            string question,
            DateTimeOffset? startsAt,
            DateTimeOffset? endsAt) : base(id)
        {
            ExternalId = externalId;
            Slug = slug;
            ConditionId = conditionId;
            Question = question;
            StartsAt = startsAt;
            EndsAt = endsAt;
        }

        public ExternalMarketId ExternalId { get; private set; } = null!;
        public MarketSlug Slug { get; private set; } = null!;
        public ConditionId ConditionId { get; private set; } = null!;
        public string Question { get; private set; } = string.Empty;
        public DateTimeOffset? StartsAt { get; private set; }
        public DateTimeOffset? EndsAt { get; private set; }
        public IReadOnlyCollection<MarketToken> Tokens => _tokens;

        public static Result<Market, Error> Create(
            MarketId id,
            ExternalMarketId externalId,
            MarketSlug slug,
            ConditionId conditionId,
            string question,
            DateTimeOffset? startsAt,
            DateTimeOffset? endsAt)
        {
            if (string.IsNullOrWhiteSpace(question))
                return GeneralErrors.ValueIsRequired(nameof(question));

            return new Market(id, externalId, slug, conditionId, question, startsAt, endsAt);
        }

        public UnitResult<Error> AddToken(
            TokenId externalTokenId,
            string outcome,
            int outcomeIndex)
        {
            if (_tokens.Any(token => token.ExternalTokenId.Equals(externalTokenId)))
                return UnitResult.Failure(MarketErrors.DuplicateTokenId(externalTokenId.Value));

            if (_tokens.Any(token => token.OutcomeIndex == outcomeIndex))
                return UnitResult.Failure(MarketErrors.DuplicateOutcomeIndex(outcomeIndex));

            var tokenResult = MarketToken.Create(Id, externalTokenId, outcome, outcomeIndex);
            if (tokenResult.IsFailure)
                return UnitResult.Failure(tokenResult.Error);

            _tokens.Add(tokenResult.Value);

            return UnitResult.Success<Error>();
        }
    }
}
