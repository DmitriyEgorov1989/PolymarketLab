using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.UseCases.Queries.GetCollectorSessionByMarket;
using PolymarketLab.SharedKernel.Errors;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.UseCases.Queries.GetCollectorSessionByMarket;

public sealed class GetCollectorSessionByMarketValidatorTests
{
    private readonly GetCollectorSessionByMarketValidator _validator = new();

    [Fact]
    public async Task Validate_WithEmptyMarketId_ShouldReturnRequiredError()
    {
        var result = await _validator.ValidateAsync(
            new GetCollectorSessionByMarketQuery(Guid.Empty));

        result.IsValid.Should().BeFalse();
        var error = Error.Deserialize(result.Errors.Single().ErrorMessage);
        error.Code.Should().Be("collector.query.market_id.required");
        error.InvalidField.Should().Be("marketId");
    }

    [Fact]
    public async Task Validate_WithMarketId_ShouldSucceed()
    {
        var result = await _validator.ValidateAsync(
            new GetCollectorSessionByMarketQuery(Guid.NewGuid()));

        result.IsValid.Should().BeTrue();
    }
}
