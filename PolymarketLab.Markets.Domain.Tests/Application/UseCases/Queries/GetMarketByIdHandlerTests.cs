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
            ExternalEventId = "event-123",
            EventSlug = "rain-event",
            ExternalMarketId = "market-123",
            MarketSlug = "will-it-rain",
            ConditionId = "0xcondition",
            Question = "Will it rain?",
            EventStartsAt = DateTimeOffset.Parse("2026-08-01T10:00:00Z"),
            EventEndsAt = DateTimeOffset.Parse("2026-08-01T12:00:00Z")
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

    private static GetMarketByIdHandler CreateHandler(IMarketRepository repository)
    {
        return new GetMarketByIdHandler(repository);
    }

    private static Market CreateMarket()
    {
        var market = Market.Create(
            MarketId.Create(Guid.NewGuid()).Value,
            ExternalEventId.Create("event-123").Value,
            EventSlug.Create("rain-event").Value,
            ExternalMarketId.Create("market-123").Value,
            MarketSlug.Create("will-it-rain").Value,
            ConditionId.Create("0xcondition").Value,
            "Will it rain?",
            DateTimeOffset.Parse("2026-07-31T10:00:00Z"),
            null,
            null,
            null,
            DateTimeOffset.Parse("2026-08-01T10:00:00Z"),
            DateTimeOffset.Parse("2026-08-01T12:00:00Z"),
            null,
            DateTimeOffset.Parse("2026-07-31T10:00:00Z")).Value;

        market.AddToken(TokenId.Create("token-yes").Value, "Yes", 0);
        market.AddToken(TokenId.Create("token-no").Value, "No", 1);

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

        public Task<Market?> GetByEventSlugAsync(EventSlug eventSlug, CancellationToken cancellationToken)
        {
            return Task.FromResult(_markets.SingleOrDefault(market => market.EventSlug.Equals(eventSlug)));
        }

        public Task<Market?> GetByExternalEventIdAsync(
            ExternalEventId externalEventId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                _markets.SingleOrDefault(market => market.ExternalEventId.Equals(externalEventId)));
        }

        public Task<Market?> GetBySlugAsync(MarketSlug slug, CancellationToken cancellationToken)
        {
            return Task.FromResult(_markets.SingleOrDefault(market => market.MarketSlug.Equals(slug)));
        }

        public Task<Market?> GetByExternalIdAsync(
            ExternalMarketId externalMarketId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                _markets.SingleOrDefault(market => market.ExternalMarketId.Equals(externalMarketId)));
        }

        public Task<Market?> GetByConditionIdAsync(ConditionId conditionId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_markets.SingleOrDefault(market => market.ConditionId.Equals(conditionId)));
        }

        public Task<IReadOnlyCollection<Market>> GetByAnyTokenIdsAsync(
            IReadOnlyCollection<TokenId> tokenIds,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyCollection<Market>>([]);
        }

        public Task<Result<MarketInsertStatus, Error>> TryAddAsync(
            Market market,
            CancellationToken cancellationToken)
        {
            _markets.Add(market);
            return Task.FromResult<Result<MarketInsertStatus, Error>>(MarketInsertStatus.Inserted);
        }

        public Task<UnitResult<Error>> UpdateScheduleAsync(
            Market market,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(UnitResult.Success<Error>());
        }
    }
}
