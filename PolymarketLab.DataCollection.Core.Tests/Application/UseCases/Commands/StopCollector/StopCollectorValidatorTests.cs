using FluentAssertions;
using FluentValidation.TestHelper;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StopCollector;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.UseCases.Commands.StopCollector;

public sealed class StopCollectorValidatorTests
{
    private readonly StopCollectorValidator _validator = new();

    [Fact]
    public void Validate_WithEmptySessionId_ShouldReturnValidationError()
    {
        var result = _validator.TestValidate(new StopCollectorCommand(Guid.Empty));

        result.ShouldHaveValidationErrorFor(command => command.SessionId)
            .WithErrorMessage(StopCollectorErrors.SessionIdRequired.Serialize());
    }

    [Fact]
    public void Validate_WithSessionId_ShouldSucceed()
    {
        var result = _validator.TestValidate(new StopCollectorCommand(Guid.NewGuid()));

        result.IsValid.Should().BeTrue();
    }
}
