using FluentAssertions;
using FluentValidation.TestHelper;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.DataCollection.Core.Application.UseCases.Commands.ReplayNormalization;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.UseCases.Commands.ReplayNormalization;

public sealed class ReplayNormalizationValidatorTests
{
    private readonly ReplayNormalizationValidator validator = new();

    [Theory]
    [InlineData(0, 2, nameof(ReplayNormalizationCommand.SourceProjectionVersion))]
    [InlineData(1, 1, nameof(ReplayNormalizationCommand.TargetProjectionVersion))]
    [InlineData(2, 1, nameof(ReplayNormalizationCommand.TargetProjectionVersion))]
    public void Validate_InvalidVersions_ShouldFail(
        int sourceVersion,
        int targetVersion,
        string propertyName)
    {
        var result = validator.TestValidate(new ReplayNormalizationCommand(
            sourceVersion,
            targetVersion,
            null,
            null));

        result.Errors.Should().Contain(error => error.PropertyName == propertyName);
    }

    [Fact]
    public void Validate_EmptySessionId_ShouldFail()
    {
        var result = validator.TestValidate(new ReplayNormalizationCommand(1, 2, Guid.Empty, null));

        result.ShouldHaveValidationErrorFor(command => command.SessionId)
            .WithErrorMessage(ReplayNormalizationErrors.SessionIdInvalid.Serialize());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_InvalidEventType_ShouldFail(string eventType)
    {
        var result = validator.TestValidate(new ReplayNormalizationCommand(1, 2, null, eventType));

        result.ShouldHaveValidationErrorFor(command => command.EventType)
            .WithErrorMessage(ReplayNormalizationErrors.EventTypeInvalid.Serialize());
    }

    [Fact]
    public void Validate_ValidComposableFilters_ShouldSucceed()
    {
        var result = validator.TestValidate(new ReplayNormalizationCommand(
            1,
            2,
            Guid.NewGuid(),
            "book"));

        result.IsValid.Should().BeTrue();
    }
}
