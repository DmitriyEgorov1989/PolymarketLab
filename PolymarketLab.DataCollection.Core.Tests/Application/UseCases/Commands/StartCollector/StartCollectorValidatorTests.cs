using FluentAssertions;
using FluentValidation.TestHelper;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StartCollector;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.UseCases.Commands.StartCollector;

public sealed class StartCollectorValidatorTests
{
    private readonly StartCollectorValidator _validator = new();

    [Fact]
    public void Validate_WithEmptyMarketId_ShouldReturnRequiredError()
    {
        var result = _validator.TestValidate(new StartCollectorCommand(Guid.Empty));

        result.ShouldHaveValidationErrorFor(command => command.MarketId)
            .WithErrorMessage(StartCollectorErrors.MarketIdRequired.Serialize());
    }

    [Fact]
    public void Validate_WithMarketId_ShouldSucceed()
    {
        var result = _validator.TestValidate(new StartCollectorCommand(Guid.NewGuid()));

        result.IsValid.Should().BeTrue();
    }
}
