using FluentAssertions;
using PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;
using MarketModel = PolymarketLab.Markets.Core.Domain.Models.Market.MarketAggregate.Market;

namespace PolymarketLab.Markets.Domain.Tests.Domain.Models.MarketAggregate;

public class MarketTests
{
    [Fact]
    public void Create_WithValidData_ShouldCreateMarket()
    {
        var id = MarketId.Create(Guid.NewGuid()).Value;
        var externalId = ExternalMarketId.Create("market-123").Value;
        var slug = MarketSlug.Create("will-it-rain").Value;
        var conditionId = ConditionId.Create("0xcondition").Value;
        var startsAt = DateTimeOffset.UtcNow;
        var endsAt = startsAt.AddDays(1);

        var result = MarketModel.Create(
            id,
            externalId,
            slug,
            conditionId,
            "Will it rain?",
            startsAt,
            endsAt);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(id);
        result.Value.ExternalId.Should().Be(externalId);
        result.Value.Slug.Should().Be(slug);
        result.Value.ConditionId.Should().Be(conditionId);
        result.Value.Question.Should().Be("Will it rain?");
        result.Value.StartsAt.Should().Be(startsAt);
        result.Value.EndsAt.Should().Be(endsAt);
        result.Value.Tokens.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_WithEmptyQuestion_ShouldReturnError(string? question)
    {
        var id = MarketId.Create(Guid.NewGuid()).Value;
        var externalId = ExternalMarketId.Create("market-123").Value;
        var slug = MarketSlug.Create("will-it-rain").Value;
        var conditionId = ConditionId.Create("0xcondition").Value;

        var result = MarketModel.Create(id, externalId, slug, conditionId, question!, null, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.ValueIsRequired);
    }

    [Fact]
    public void CompareTo_ShouldCompareUnderlyingValues()
    {
        var first = MarketId.Create(Guid.Parse("00000000-0000-0000-0000-000000000001")).Value;
        var second = MarketId.Create(Guid.Parse("00000000-0000-0000-0000-000000000002")).Value;

        first.CompareTo(second).Should().BeLessThan(0);
    }

    [Fact]
    public void AddToken_WithValidData_ShouldAddTokenToMarket()
    {
        var market = CreateMarket();
        var tokenId = TokenId.Create("token-yes").Value;

        var result = market.AddToken(tokenId, "Yes", 0);

        result.IsSuccess.Should().BeTrue();
        market.Tokens.Should().ContainSingle();
        market.Tokens.Single().ExternalTokenId.Should().Be(tokenId);
    }

    [Fact]
    public void AddToken_WithDuplicateTokenId_ShouldReturnConflict()
    {
        var market = CreateMarket();
        var tokenId = TokenId.Create("token-yes").Value;
        market.AddToken(tokenId, "Yes", 0);

        var result = market.AddToken(tokenId, "No", 1);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("market.token.external_id.duplicate");
        result.Error.Type.Should().Be(ErrorType.Conflict);
        market.Tokens.Should().ContainSingle();
    }

    [Fact]
    public void AddToken_WithDuplicateOutcomeIndex_ShouldReturnConflict()
    {
        var market = CreateMarket();
        market.AddToken(TokenId.Create("token-yes").Value, "Yes", 0);

        var result = market.AddToken(TokenId.Create("token-no").Value, "No", 0);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("market.token.outcome_index.duplicate");
        result.Error.Type.Should().Be(ErrorType.Conflict);
        market.Tokens.Should().ContainSingle();
    }

    [Theory]
    [InlineData(null, 1, ErrorType.ValueIsRequired)]
    [InlineData("", 1, ErrorType.ValueIsRequired)]
    [InlineData("No", -1, ErrorType.ValueIsInvalid)]
    public void AddToken_WithInvalidTokenData_ShouldReturnError(
        string? outcome,
        int outcomeIndex,
        ErrorType expectedType)
    {
        var market = CreateMarket();

        var result = market.AddToken(
            TokenId.Create("token-no").Value,
            outcome!,
            outcomeIndex);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(expectedType);
        market.Tokens.Should().BeEmpty();
    }

    private static MarketModel CreateMarket()
    {
        return MarketModel.Create(
            MarketId.Create(Guid.NewGuid()).Value,
            ExternalMarketId.Create("market-123").Value,
            MarketSlug.Create("will-it-rain").Value,
            ConditionId.Create("0xcondition").Value,
            "Will it rain?",
            null,
            null).Value;
    }
}
