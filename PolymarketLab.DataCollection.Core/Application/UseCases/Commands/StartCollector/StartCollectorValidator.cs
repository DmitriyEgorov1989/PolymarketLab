using FluentValidation;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.SharedKernel.Extensions.Validations;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StartCollector;

public sealed class StartCollectorValidator : AbstractValidator<StartCollectorCommand>
{
    public StartCollectorValidator()
    {
        RuleFor(command => command.MarketId)
            .NotEmpty()
            .WithError(StartCollectorErrors.MarketIdRequired);
    }
}
