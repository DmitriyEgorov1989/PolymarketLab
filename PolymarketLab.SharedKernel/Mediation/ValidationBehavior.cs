using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using PolymarketLab.SharedKernel.Extensions.Validations;
using ErrorList = PolymarketLab.SharedKernel.Errors.Error.ErrorList;

namespace PolymarketLab.SharedKernel.Mediation;

public sealed class ValidationBehavior<TRequest, TValue>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, Result<TValue, ErrorList>>
    where TRequest : notnull, IRequest<Result<TValue, ErrorList>>
{
    public async Task<Result<TValue, ErrorList>> Handle(
        TRequest request,
        RequestHandlerDelegate<Result<TValue, ErrorList>> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
            return await next(cancellationToken);

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(
            validators.Select(validator => validator.ValidateAsync(context, cancellationToken)));
        var failures = results
            .SelectMany(result => result.Errors)
            .ToList();

        if (failures.Count == 0)
            return await next(cancellationToken);

        var errors = new ValidationResult(failures).ToValidationErrorResponse(request);
        return Result.Failure<TValue, ErrorList>(errors);
    }
}
