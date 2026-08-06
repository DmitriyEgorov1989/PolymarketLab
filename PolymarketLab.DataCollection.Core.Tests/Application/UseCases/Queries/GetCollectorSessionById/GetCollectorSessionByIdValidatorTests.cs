using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.UseCases.Queries.GetCollectorSessionById;
using PolymarketLab.SharedKernel.Errors;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.UseCases.Queries.GetCollectorSessionById;

public sealed class GetCollectorSessionByIdValidatorTests
{
    private readonly GetCollectorSessionByIdValidator _validator = new();

    [Fact]
    public async Task Validate_WithEmptySessionId_ShouldReturnRequiredError()
    {
        var result = await _validator.ValidateAsync(
            new GetCollectorSessionByIdQuery(Guid.Empty));

        result.IsValid.Should().BeFalse();
        var error = Error.Deserialize(result.Errors.Single().ErrorMessage);
        error.Code.Should().Be("collector.query.session_id.required");
        error.InvalidField.Should().Be("sessionId");
    }

    [Fact]
    public async Task Validate_WithSessionId_ShouldSucceed()
    {
        var result = await _validator.ValidateAsync(
            new GetCollectorSessionByIdQuery(Guid.NewGuid()));

        result.IsValid.Should().BeTrue();
    }
}
