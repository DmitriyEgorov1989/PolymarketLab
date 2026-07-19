using FluentAssertions;
using PolymarketLab.Markets.Core.Domain.Models.Market.Entity;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;

namespace PolymarketLab.Markets.Domain.Tests.Domain.Models.Market.Entity;

public class MarketTokenTests
{
    [Fact]
    public void Create_WithValidData_ShouldCreateMarketToken()
    {
        var marketId = MarketId.Create(Guid.NewGuid()).Value;
        var externalTokenId = TokenId.Create("123456789").Value;

        var result = MarketToken.Create(marketId, externalTokenId, "Yes", 0);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().NotBeEmpty();
        result.Value.MarketId.Should().Be(marketId);
        result.Value.ExternalTokenId.Should().Be(externalTokenId);
        result.Value.Outcome.Should().Be("Yes");
        result.Value.OutcomeIndex.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_WithEmptyOutcome_ShouldReturnError(string? outcome)
    {
        var marketId = MarketId.Create(Guid.NewGuid()).Value;
        var externalTokenId = TokenId.Create("123456789").Value;

        var result = MarketToken.Create(marketId, externalTokenId, outcome!, 0);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.ValueIsRequired);
    }

    [Fact]
    public void Create_WithNegativeOutcomeIndex_ShouldReturnError()
    {
        var marketId = MarketId.Create(Guid.NewGuid()).Value;
        var externalTokenId = TokenId.Create("123456789").Value;

        var result = MarketToken.Create(marketId, externalTokenId, "Yes", -1);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.ValueIsInvalid);
    }
}
