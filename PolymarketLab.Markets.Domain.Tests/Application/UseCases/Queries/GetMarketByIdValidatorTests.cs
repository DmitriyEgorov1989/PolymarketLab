using FluentAssertions;
using PolymarketLab.Markets.Core.Application.UseCases.Queries.GetMarketById;
using PolymarketLab.SharedKernel.Errors;
using Xunit;

namespace PolymarketLab.Markets.Domain.Tests.Application.UseCases.Queries;

public sealed class GetMarketByIdValidatorTests
{
    private readonly GetMarketByIdValidator _validator = new();

    [Fact]
    public async Task Validate_WithEmptyMarketId_ShouldReturnRequiredError()
    {
        var result = await _validator.ValidateAsync(new GetMarketByIdQuery(Guid.Empty));

        result.IsValid.Should().BeFalse();
        var error = Error.Deserialize(result.Errors.Single().ErrorMessage);
        error.Code.Should().Be("market.query.market_id.required");
        error.InvalidField.Should().Be("marketId");
    }

    [Fact]
    public async Task Validate_WithMarketId_ShouldSucceed()
    {
        var result = await _validator.ValidateAsync(new GetMarketByIdQuery(Guid.NewGuid()));

        result.IsValid.Should().BeTrue();
    }
}
