using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.SharedKernel.DomainModels.Ids
{
    public class ConditionId : ValueObject
    {
        public string Value { get; init; }
        private ConditionId(string value)
        {
            Value = value;
        }
        public static Result<ConditionId, Error> Create(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return GeneralErrors.ValueIsInvalid(nameof(value));
            return new ConditionId(value);
        }
        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return Value;
        }
    }
}
