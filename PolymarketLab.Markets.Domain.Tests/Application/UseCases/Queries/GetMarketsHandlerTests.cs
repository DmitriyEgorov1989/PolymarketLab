using CSharpFunctionalExtensions;
using FluentAssertions;
using PolymarketLab.Markets.Core.Application.UseCases.Queries.GetMarkets;
using PolymarketLab.Markets.Core.Domain.Models.Market.MarketAggregate;
using PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects;
using PolymarketLab.Markets.Core.Ports;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;

namespace PolymarketLab.Markets.Domain.Tests.Application.UseCases.Queries;

public sealed class GetMarketsHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-01T11:00:00Z");

    [Fact]
    public async Task Handle_WithStoredMarkets_ShouldReturnMappedMarkets()
    {
        var first = CreateMarket("alpha-market", "market-111", "0x111", "Alpha question?");
        var second = CreateMarket("beta-market", "market-222", "0x222", "Beta question?");
        var repository = new InMemoryMarketRepository(first, second);
        var handler = new GetMarketsHandler(repository, new FixedTimeProvider(Now));

        var result = await handler.Handle(new GetMarketsQuery(), CancellationToken.None);
        var markets = result.Value.Markets.ToArray();

        result.IsSuccess.Should().BeTrue();
        markets.Should().HaveCount(2);
        markets[0].Should().BeEquivalentTo(new
        {
            MarketId = first.Id.Value,
            ExternalMarketId = "market-111",
            Slug = "alpha-market",
            ConditionId = "0x111",
            Question = "Alpha question?",
            StartsAt = (DateTimeOffset?)DateTimeOffset.Parse("2026-08-01T10:00:00Z"),
            EndsAt = (DateTimeOffset?)DateTimeOffset.Parse("2026-08-01T12:00:00Z")
        });
        markets[0].Tokens.Should().BeEquivalentTo(
        [
            new { TokenId = "alpha-market-yes", Outcome = "Yes", OutcomeIndex = 0 },
            new { TokenId = "alpha-market-no", Outcome = "No", OutcomeIndex = 1 }
        ], options => options.WithStrictOrdering());
        markets[1].Should().BeEquivalentTo(new
        {
            MarketId = second.Id.Value,
            ExternalMarketId = "market-222",
            Slug = "beta-market",
            ConditionId = "0x222",
            Question = "Beta question?",
            StartsAt = (DateTimeOffset?)DateTimeOffset.Parse("2026-08-01T10:00:00Z"),
            EndsAt = (DateTimeOffset?)DateTimeOffset.Parse("2026-08-01T12:00:00Z")
        });
        markets[1].Tokens.Should().BeEquivalentTo(
        [
            new { TokenId = "beta-market-yes", Outcome = "Yes", OutcomeIndex = 0 },
            new { TokenId = "beta-market-no", Outcome = "No", OutcomeIndex = 1 }
        ], options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Handle_WithoutStoredMarkets_ShouldReturnEmptyCollection()
    {
        var handler = new GetMarketsHandler(
            new InMemoryMarketRepository(),
            new FixedTimeProvider(Now));

        var result = await handler.Handle(new GetMarketsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Markets.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldHideMarketsOutsideCollectionWindow()
    {
        var future = CreateMarket("future", "market-future", "0xfuture", "Future?",
            startsAt: Now.AddMinutes(1), endsAt: Now.AddHours(1));
        var ended = CreateMarket("ended", "market-ended", "0xended", "Ended?",
            startsAt: Now.AddHours(-1), endsAt: Now);
        var openEnded = CreateMarket("open", "market-open", "0xopen", "Open?",
            hasCollectionWindow: false);
        var handler = new GetMarketsHandler(
            new InMemoryMarketRepository(future, ended, openEnded),
            new FixedTimeProvider(Now));

        var result = await handler.Handle(new GetMarketsQuery(), CancellationToken.None);

        result.Value.Markets.Select(market => market.Slug).Should().Equal("open");
    }

    private static Market CreateMarket(
        string slug,
        string externalId,
        string conditionId,
        string question,
        DateTimeOffset? startsAt = null,
        DateTimeOffset? endsAt = null,
        bool hasCollectionWindow = true)
    {
        var market = Market.Create(
            MarketId.Create(Guid.NewGuid()).Value,
            ExternalMarketId.Create(externalId).Value,
            MarketSlug.Create(slug).Value,
            ConditionId.Create(conditionId).Value,
            question,
            hasCollectionWindow
                ? startsAt ?? DateTimeOffset.Parse("2026-08-01T10:00:00Z")
                : null,
            hasCollectionWindow
                ? endsAt ?? DateTimeOffset.Parse("2026-08-01T12:00:00Z")
                : null).Value;

        market.AddToken(TokenId.Create($"{slug}-yes").Value, "Yes", 0);
        market.AddToken(TokenId.Create($"{slug}-no").Value, "No", 1);

        return market;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class InMemoryMarketRepository(params Market[] markets) : IMarketRepository
    {
        private readonly List<Market> _markets = [.. markets];

        public Task<IReadOnlyCollection<Market>> GetAllAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyCollection<Market>>(_markets);
        }

        public Task<Market?> GetByIdAsync(MarketId marketId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_markets.SingleOrDefault(market => market.Id.Equals(marketId)));
        }

        public Task<Market?> GetBySlugAsync(MarketSlug slug, CancellationToken cancellationToken)
        {
            return Task.FromResult(_markets.SingleOrDefault(market => market.Slug.Equals(slug)));
        }

        public Task<Market?> GetByExternalIdAsync(
            ExternalMarketId externalMarketId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_markets.SingleOrDefault(market => market.ExternalId.Equals(externalMarketId)));
        }

        public Task<Market?> GetByConditionIdAsync(ConditionId conditionId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_markets.SingleOrDefault(market => market.ConditionId.Equals(conditionId)));
        }

        public Task<Result<MarketInsertStatus, Error>> TryAddAsync(
            Market market,
            CancellationToken cancellationToken)
        {
            _markets.Add(market);
            return Task.FromResult<Result<MarketInsertStatus, Error>>(MarketInsertStatus.Inserted);
        }
    }
}
