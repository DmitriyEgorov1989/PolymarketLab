using CSharpFunctionalExtensions;
using FluentValidation;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.SharedKernel.Extensions.Validations;

public static class CustomValidation
{
    public static IRuleBuilderOptionsConditions<T, TElement> MustBeValueObject<T, TElement, TValueObject>(
        this IRuleBuilder<T, TElement> ruleBuilder, Func<TElement, Result<TValueObject, Error>> factoryMethod)
    {
        return ruleBuilder.Custom((value, context) =>
        {
            var result = factoryMethod(value);

            if (result.IsSuccess)
                return;

            context.AddFailure(result.Error.Serialize());
        });
    }

    public static IRuleBuilderOptions<T, TElement> WithError<T, TElement>(
        this IRuleBuilderOptions<T, TElement> ruleBuilder, Error error)
    {
        return ruleBuilder.WithMessage(error.Serialize());
    }
}
