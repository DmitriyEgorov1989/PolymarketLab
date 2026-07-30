using FluentValidation;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.SharedKernel.Extensions.Validations;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StopCollector;

public sealed class StopCollectorValidator : AbstractValidator<StopCollectorCommand>
{
    public StopCollectorValidator()
    {
        RuleFor(command => command.SessionId)
            .NotEmpty()
            .WithError(StopCollectorErrors.SessionIdRequired);
    }
}
