using FluentValidation;
using PolymarketLab.SharedKernel.Errors;
using PolymarketLab.SharedKernel.Extensions.Validations;

namespace PolymarketLab.Markets.Core.Application.UseCases.Commands
{
    public sealed class RegisterCommandValidation : AbstractValidator<RegisterMarketCommand>
    {
        public RegisterCommandValidation()
        {
            var requiredError = GeneralErrors.ValueIsRequired(nameof(RegisterMarketCommand.MarketUri));

            RuleFor(command => command.MarketUri)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .WithError(requiredError)
                .Must(marketUri => !string.IsNullOrWhiteSpace(marketUri))
                .WithError(requiredError);
        }
    }
}
