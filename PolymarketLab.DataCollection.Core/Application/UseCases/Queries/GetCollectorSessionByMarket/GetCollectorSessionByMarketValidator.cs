using FluentValidation;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.SharedKernel.Extensions.Validations;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Queries.GetCollectorSessionByMarket;

public sealed class GetCollectorSessionByMarketValidator : AbstractValidator<GetCollectorSessionByMarketQuery>
{
    public GetCollectorSessionByMarketValidator()
    {
        RuleFor(query => query.MarketId)
            .NotEmpty()
            .WithError(CollectorSessionQueryErrors.MarketIdRequired);
    }
}
