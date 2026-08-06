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
    [Fact]
    public async Task Handle_WithStoredMarkets_ShouldReturnMappedMarkets()
    {
        var first = CreateMarket("alpha-market", "market-111", "0x111", "Alpha question?");
        var second = CreateMarket("beta-market", "market-222", "0x222", "Beta question?");
        var repository = new InMemoryMarketRepository(first, second);
        var handler = new GetMarketsHandler(repository);

        var result = await handler.Handle(new GetMarketsQuery(), CancellationToken.None);
        var markets = result.Value.Markets.ToArray();

        result.IsSuccess.Should().BeTrue();
        markets.Should().HaveCount(2);
        markets[0].MarketId.Should().Be(first.Id.Value);
        markets[0].Slug.Should().Be("alpha-market");
        markets[0].Tokens.Should().HaveCount(2);
        markets[1].MarketId.Should().Be(second.Id.Value);
    }

    [Fact]
    public async Task Handle_WithoutStoredMarkets_ShouldReturnEmptyCollection()
    {
        var handler = new GetMarketsHandler(new InMemoryMarketRepository());

        var result = await handler.Handle(new GetMarketsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Markets.Should().BeEmpty();
    }

    private static Market CreateMarket(
        string slug,
        string externalId,
        string conditionId,
        string question)
    {
        var market = Market.Create(
            MarketId.Create(Guid.NewGuid()).Value,
            ExternalMarketId.Create(externalId).Value,
            MarketSlug.Create(slug).Value,
            ConditionId.Create(conditionId).Value,
            question,
            DateTimeOffset.Parse("2026-08-01T10:00:00Z"),
            DateTimeOffset.Parse("2026-08-01T12:00:00Z")).Value;

        market.AddToken(TokenId.Create($"{slug}-yes").Value, "Yes", 0);
        market.AddToken(TokenId.Create($"{slug}-no").Value, "No", 1);

        return market;
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
