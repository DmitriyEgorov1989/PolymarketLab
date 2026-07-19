using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.SharedKernel.DomainModels.Ids
{
    public class ExternalMarketId : ValueObject
    {
        public string Value { get; init; }
        private ExternalMarketId(string value)
        {
            Value = value;
        }
        public static Result<ExternalMarketId, Error> Create(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return GeneralErrors.ValueIsInvalid(nameof(value));
            return new ExternalMarketId(value);
        }
        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return Value;
        }
    }
}
