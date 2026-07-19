using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.SharedKernel.DomainModels.Ids
{
    public class TokenId : ValueObject
    {
        public string Value { get; init; }

        private TokenId(string value)
        {
            Value = value;
        }

        public static Result<TokenId, Error> Create(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return GeneralErrors.ValueIsInvalid(nameof(value));

            return new TokenId(value);
        }

        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return Value;
        }
    }
}
