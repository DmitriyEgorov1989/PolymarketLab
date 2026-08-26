using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects
{
    /// <summary>
    ///     Identifies a Polymarket event independently from its child market.
    /// </summary>
    public sealed class EventSlug : ValueObject
    {
        /// <summary>
        ///     Gets the non-empty event slug supplied by Polymarket.
        /// </summary>
        public string Value { get; }

        private EventSlug(string value)
        {
            Value = value;
        }

        /// <summary>
        ///     Creates an event slug from a non-empty external value.
        /// </summary>
        /// <param name="value">The Polymarket event slug.</param>
        /// <returns>The event slug, or a validation error when <paramref name="value"/> is empty.</returns>
        public static Result<EventSlug, Error> Create(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return GeneralErrors.ValueIsInvalid(nameof(value));

            return new EventSlug(value);
        }

        /// <inheritdoc />
        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return Value;
        }
    }
}
