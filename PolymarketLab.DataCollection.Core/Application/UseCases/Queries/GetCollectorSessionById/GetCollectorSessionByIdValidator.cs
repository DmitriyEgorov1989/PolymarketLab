using FluentValidation;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.SharedKernel.Extensions.Validations;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Queries.GetCollectorSessionById;

public sealed class GetCollectorSessionByIdValidator : AbstractValidator<GetCollectorSessionByIdQuery>
{
    public GetCollectorSessionByIdValidator()
    {
        RuleFor(query => query.SessionId)
            .NotEmpty()
            .WithError(CollectorSessionQueryErrors.SessionIdRequired);
    }
}
