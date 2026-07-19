using FluentAssertions;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;

namespace PolymarketLab.Markets.Domain.Tests.SharedKernel.DomainModels.Ids;

public class ExternalIdsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ConditionId_Create_WithEmptyValue_ShouldReturnError(string? value)
    {
        var result = ConditionId.Create(value!);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.ValueIsInvalid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ExternalMarketId_Create_WithEmptyValue_ShouldReturnError(string? value)
    {
        var result = ExternalMarketId.Create(value!);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.ValueIsInvalid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void TokenId_Create_WithEmptyValue_ShouldReturnError(string? value)
    {
        var result = TokenId.Create(value!);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.ValueIsInvalid);
    }
}
