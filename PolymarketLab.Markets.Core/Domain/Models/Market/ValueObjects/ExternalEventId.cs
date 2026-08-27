using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects;

/// <summary>
///     Идентифицирует событие Polymarket независимо от его дочернего рынка.
/// </summary>
public sealed class ExternalEventId : ValueObject
{
    private ExternalEventId(string value)
    {
        Value = value;
    }

    /// <summary>
    ///     Возвращает непустой идентификатор события Gamma.
    /// </summary>
    public string Value { get; }

    /// <summary>
    ///     Создаёт идентификатор события из непустого внешнего значения.
    /// </summary>
    /// <param name="value">Идентификатор события Gamma.</param>
    /// <returns>Идентификатор либо ошибка валидации, если <paramref name="value"/> пуст.</returns>
    public static Result<ExternalEventId, Error> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return GeneralErrors.ValueIsInvalid(nameof(value));

        return new ExternalEventId(value);
    }

    /// <inheritdoc />
    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
