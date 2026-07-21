using FluentAssertions;
using PolymarketLab.Markets.Core.Application.UseCases.Commands;
using PolymarketLab.SharedKernel.Errors;
using Xunit;

namespace PolymarketLab.Markets.Domain.Tests.Application.UseCases.Commands;

public sealed class RegisterCommandValidationTests
{
    private readonly RegisterCommandValidation _validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Validate_WithMissingMarketUri_ShouldReturnRequiredError(string? marketUri)
    {
        var command = new RegisterMarketCommand(marketUri!);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors[0].PropertyName.Should().Be(nameof(RegisterMarketCommand.MarketUri));
        result.Errors[0].ErrorMessage.Should().Be(
            GeneralErrors.ValueIsRequired(nameof(RegisterMarketCommand.MarketUri)).Serialize());
    }

    [Fact]
    public void Validate_WithNonEmptyMarketUri_ShouldSucceedWithoutCheckingUrlFormat()
    {
        var command = new RegisterMarketCommand("not-a-url");

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }
}
