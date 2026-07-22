using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.SharedKernel.DomainModels.Ids
{
    public class CollectorSessionId : ValueObject, IComparable<CollectorSessionId>
    {
        public Guid Value { get; init; }

        private CollectorSessionId(Guid value)
        {
            Value = value;
        }

        public static Result<CollectorSessionId, Error> Create(Guid value)
        {
            if (value == default)
                return GeneralErrors.ValueIsInvalid(nameof(value));

            return new CollectorSessionId(value);
        }

        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return Value;
        }

        public int CompareTo(CollectorSessionId? other)
        {
            return other is null ? 1 : Value.CompareTo(other.Value);
        }
    }
}
