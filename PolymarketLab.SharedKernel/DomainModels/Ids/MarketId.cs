using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.Errors;


namespace PolymarketLab.SharedKernel.DomainModels.Ids
{
    public class MarketId : ValueObject, IComparable<MarketId>
    {
        public Guid Value { get; init; }
        private MarketId(Guid value)
        {
            Value = value;
        }
        public static Result<MarketId, Error> Create(Guid value)
        {
            if (value == default)
                return GeneralErrors.ValueIsInvalid(nameof(value));
            return new MarketId(value);
        }
        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return Value;
        }

        public int CompareTo(MarketId? other)
        {
            return other is null ? 1 : Value.CompareTo(other.Value);
        }
    }
}
