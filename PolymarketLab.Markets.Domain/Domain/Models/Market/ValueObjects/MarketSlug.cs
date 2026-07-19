using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects
{
    public class MarketSlug : ValueObject
    {
        public string Value { get; init; }

        private MarketSlug(string value)
        {
            Value = value;
        }

        public static Result<MarketSlug, Error> Create(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return GeneralErrors.ValueIsInvalid(nameof(value));

            return new MarketSlug(value);
        }

        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return Value;
        }
    }
}
