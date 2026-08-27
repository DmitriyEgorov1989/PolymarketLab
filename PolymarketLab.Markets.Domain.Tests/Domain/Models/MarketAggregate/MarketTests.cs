using FluentAssertions;
using PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;
using MarketModel = PolymarketLab.Markets.Core.Domain.Models.Market.MarketAggregate.Market;

namespace PolymarketLab.Markets.Domain.Tests.Domain.Models.MarketAggregate;

public class MarketTests
{
    private static readonly DateTimeOffset DiscoveredAt = DateTimeOffset.Parse("2026-08-27T09:00:00+03:00");
    private static readonly DateTimeOffset EventStartsAt = DateTimeOffset.Parse("2026-08-27T10:00:00+03:00");
    private static readonly DateTimeOffset EventEndsAt = EventStartsAt.AddMinutes(5);

    [Fact]
    public void Create_WithValidData_ShouldCreateMarketAndNormalizeTimestampsToUtc()
    {
        var market = CreateMarket();

        market.ExternalEventId.Value.Should().Be("event-123");
        market.EventSlug.Value.Should().Be("bitcoin-up-or-down");
        market.ExternalMarketId.Value.Should().Be("market-123");
        market.MarketSlug.Value.Should().Be("bitcoin-up-or-down-5m");
        market.ConditionId.Value.Should().Be("0xcondition");
        market.Question.Should().Be("Will Bitcoin go up?");
        market.DiscoveredAt.Should().Be(DiscoveredAt.ToUniversalTime());
        market.ExternalCreatedAt.Should().Be(DiscoveredAt.AddDays(-1).ToUniversalTime());
        market.OrdersOpenedAt.Should().Be(DiscoveredAt.AddMinutes(10).ToUniversalTime());
        market.GammaStartDate.Should().Be(EventStartsAt.AddMinutes(-1).ToUniversalTime());
        market.EventStartsAt.Should().Be(EventStartsAt.ToUniversalTime());
        market.EventEndsAt.Should().Be(EventEndsAt.ToUniversalTime());
        market.ExternalClosedAt.Should().BeNull();
        market.ScheduleRefreshedAt.Should().Be(DiscoveredAt.ToUniversalTime());
        market.Tokens.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_WithEmptyQuestion_ShouldReturnError(string? question)
    {
        var result = CreateMarketResult(question!);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.ValueIsRequired);
    }

    [Fact]
    public void Create_WithEventEndNotAfterStart_ShouldReturnError()
    {
        var result = CreateMarketResult(eventEndsAt: EventStartsAt);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.ValueIsInvalid);
    }

    [Fact]
    public void HasSameIdentity_WithExactOrderedTokens_ShouldReturnTrue()
    {
        var first = CreateMarket();
        first.AddToken(TokenId.Create("token-yes").Value, "Yes", 0);
        first.AddToken(TokenId.Create("token-no").Value, "No", 1);
        var second = CreateMarket();
        second.AddToken(TokenId.Create("token-no").Value, "No", 1);
        second.AddToken(TokenId.Create("token-yes").Value, "Yes", 0);

        first.HasSameIdentity(second).Should().BeTrue();
    }

    [Fact]
    public void HasSameIdentity_WithChangedTokenOutcome_ShouldReturnFalse()
    {
        var first = CreateMarket();
        first.AddToken(TokenId.Create("token-yes").Value, "Yes", 0);
        var second = CreateMarket();
        second.AddToken(TokenId.Create("token-yes").Value, "Up", 0);

        first.HasSameIdentity(second).Should().BeFalse();
    }

    [Fact]
    public void RefreshSchedule_ShouldPreserveDiscoveryAndUpdateScheduleInUtc()
    {
        var market = CreateMarket();
        var discoveredAt = market.DiscoveredAt;
        var refreshedAt = DateTimeOffset.Parse("2026-08-27T12:00:00+03:00");
        var newStart = EventStartsAt.AddMinutes(5);
        var newEnd = EventEndsAt.AddMinutes(5);

        var result = market.RefreshSchedule(
            externalCreatedAt: null,
            ordersOpenedAt: refreshedAt.AddMinutes(-30),
            gammaStartDate: newStart.AddMinutes(-1),
            eventStartsAt: newStart,
            eventEndsAt: newEnd,
            externalClosedAt: refreshedAt,
            scheduleRefreshedAt: refreshedAt);

        result.IsSuccess.Should().BeTrue();
        market.DiscoveredAt.Should().Be(discoveredAt);
        market.ExternalCreatedAt.Should().BeNull();
        market.EventStartsAt.Should().Be(newStart.ToUniversalTime());
        market.EventEndsAt.Should().Be(newEnd.ToUniversalTime());
        market.ExternalClosedAt.Should().Be(refreshedAt.ToUniversalTime());
        market.ScheduleRefreshedAt.Should().Be(refreshedAt.ToUniversalTime());
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

        var result = market.AddToken(TokenId.Create("token-no").Value, outcome!, outcomeIndex);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(expectedType);
        market.Tokens.Should().BeEmpty();
    }

    private static MarketModel CreateMarket()
    {
        return CreateMarketResult().Value;
    }

    private static CSharpFunctionalExtensions.Result<MarketModel, Error> CreateMarketResult(
        string question = "Will Bitcoin go up?",
        DateTimeOffset? eventEndsAt = null)
    {
        return MarketModel.Create(
            MarketId.Create(Guid.NewGuid()).Value,
            ExternalEventId.Create("event-123").Value,
            EventSlug.Create("bitcoin-up-or-down").Value,
            ExternalMarketId.Create("market-123").Value,
            MarketSlug.Create("bitcoin-up-or-down-5m").Value,
            ConditionId.Create("0xcondition").Value,
            question,
            DiscoveredAt,
            DiscoveredAt.AddDays(-1),
            DiscoveredAt.AddMinutes(10),
            EventStartsAt.AddMinutes(-1),
            EventStartsAt,
            eventEndsAt ?? EventEndsAt,
            externalClosedAt: null,
            scheduleRefreshedAt: DiscoveredAt);
    }
}
