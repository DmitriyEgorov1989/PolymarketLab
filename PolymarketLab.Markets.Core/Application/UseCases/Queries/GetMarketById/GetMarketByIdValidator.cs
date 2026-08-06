using FluentValidation;
using PolymarketLab.Markets.Core.Application.Errors;
using PolymarketLab.SharedKernel.Extensions.Validations;

namespace PolymarketLab.Markets.Core.Application.UseCases.Queries.GetMarketById;

public sealed class GetMarketByIdValidator : AbstractValidator<GetMarketByIdQuery>
{
    public GetMarketByIdValidator()
    {
        RuleFor(query => query.MarketId)
            .NotEmpty()
            .WithError(MarketQueryErrors.MarketIdRequired);
    }
}
