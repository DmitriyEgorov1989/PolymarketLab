using CSharpFunctionalExtensions;
using FluentAssertions;
using PolymarketLab.Markets.Core.Application.UseCases.Queries.GetMarketById;
using PolymarketLab.Markets.Core.Domain.Models.Market.MarketAggregate;
using PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects;
using PolymarketLab.Markets.Core.Ports;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;

namespace PolymarketLab.Markets.Domain.Tests.Application.UseCases.Queries;

public sealed class GetMarketByIdHandlerTests
{
    [Fact]
    public async Task Handle_WithExistingMarket_ShouldReturnMappedMarket()
    {
        var market = CreateMarket();
        var handler = CreateHandler(new InMemoryMarketRepository(market));

        var result = await handler.Handle(new GetMarketByIdQuery(market.Id.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Market.Should().BeEquivalentTo(new
        {
            MarketId = market.Id.Value,
            ExternalMarketId = "market-123",
            Slug = "will-it-rain",
            ConditionId = "0xcondition",
            Question = "Will it rain?",
            StartsAt = (DateTimeOffset?)DateTimeOffset.Parse("2026-08-01T10:00:00Z"),
            EndsAt = (DateTimeOffset?)DateTimeOffset.Parse("2026-08-01T12:00:00Z")
        });
        result.Value.Market.Tokens.Should().BeEquivalentTo(
        [
            new { TokenId = "token-yes", Outcome = "Yes", OutcomeIndex = 0 },
            new { TokenId = "token-no", Outcome = "No", OutcomeIndex = 1 }
        ], options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Handle_WithMissingMarket_ShouldReturnNotFound()
    {
        var handler = CreateHandler(new InMemoryMarketRepository());
        var marketId = Guid.NewGuid();

        var result = await handler.Handle(new GetMarketByIdQuery(marketId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Code.Should().Be("market.query.not_found");
    }

    [Fact]
    public async Task Handle_WithEmptyGuid_ShouldReturnRequiredErrorWithoutCallingRepository()
    {
        var repository = new InMemoryMarketRepository();
        var handler = CreateHandler(repository);

        var result = await handler.Handle(new GetMarketByIdQuery(Guid.Empty), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Code.Should().Be("market.query.market_id.required");
        result.Error.Single().Type.Should().Be(ErrorType.ValueIsRequired);
        repository.GetByIdCallCount.Should().Be(0);
    }

    private static GetMarketByIdHandler CreateHandler(IMarketRepository repository)
    {
        return new GetMarketByIdHandler(new GetMarketByIdValidator(), repository);
    }

    private static Market CreateMarket()
    {
        var market = Market.Create(
            MarketId.Create(Guid.NewGuid()).Value,
            ExternalMarketId.Create("market-123").Value,
            MarketSlug.Create("will-it-rain").Value,
            ConditionId.Create("0xcondition").Value,
            "Will it rain?",
            DateTimeOffset.Parse("2026-08-01T10:00:00Z"),
            DateTimeOffset.Parse("2026-08-01T12:00:00Z")).Value;

        market.AddToken(TokenId.Create("token-yes").Value, "Yes", 0);
        market.AddToken(TokenId.Create("token-no").Value, "No", 1);

        return market;
    }

    private sealed class InMemoryMarketRepository(params Market[] markets) : IMarketRepository
    {
        private readonly List<Market> _markets = [.. markets];

        public int GetByIdCallCount { get; private set; }

        public Task<IReadOnlyCollection<Market>> GetAllAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyCollection<Market>>(_markets);
        }

        public Task<Market?> GetByIdAsync(MarketId marketId, CancellationToken cancellationToken)
        {
            GetByIdCallCount++;
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
