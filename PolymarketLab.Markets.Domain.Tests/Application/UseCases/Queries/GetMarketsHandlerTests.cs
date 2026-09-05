using CSharpFunctionalExtensions;
using FluentAssertions;
using PolymarketLab.Markets.Core.Application.UseCases.Queries.GetMarkets;
using PolymarketLab.Markets.Core.Domain.Models.Market.MarketAggregate;
using PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects;
using PolymarketLab.Markets.Core.Ports;
using PolymarketLab.Markets.Core.Ports.Dto;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;

namespace PolymarketLab.Markets.Domain.Tests.Application.UseCases.Queries;

public sealed class GetMarketsHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-01T11:00:00Z");

    [Fact]
    public async Task Handle_WithStoredMarkets_ShouldReturnMappedMarkets()
    {
        var first = CreateMarket("alpha-market", "market-111", "0x111", "Alpha question?");
        var second = CreateMarket("beta-market", "market-222", "0x222", "Beta question?");
        var repository = new InMemoryMarketRepository(first, second);
        var handler = CreateHandler(repository);

        var result = await handler.Handle(new GetMarketsQuery(), CancellationToken.None);
        var markets = result.Value.Markets.ToArray();

        result.IsSuccess.Should().BeTrue();
        markets.Should().HaveCount(2);
        markets[0].Should().BeEquivalentTo(new
        {
            MarketId = first.Id.Value,
            ExternalEventId = "event-alpha-market",
            EventSlug = "event-alpha-market",
            ExternalMarketId = "market-111",
            MarketSlug = "alpha-market",
            ConditionId = "0x111",
            Question = "Alpha question?",
            EventStartsAt = DateTimeOffset.Parse("2026-08-01T10:00:00Z"),
            EventEndsAt = DateTimeOffset.Parse("2026-08-01T12:00:00Z")
        });
        markets[0].Tokens.Should().BeEquivalentTo(
        [
            new { TokenId = "alpha-market-yes", Outcome = "Yes", OutcomeIndex = 0 },
            new { TokenId = "alpha-market-no", Outcome = "No", OutcomeIndex = 1 }
        ], options => options.WithStrictOrdering());
        markets[1].Should().BeEquivalentTo(new
        {
            MarketId = second.Id.Value,
            ExternalEventId = "event-beta-market",
            EventSlug = "event-beta-market",
            ExternalMarketId = "market-222",
            MarketSlug = "beta-market",
            ConditionId = "0x222",
            Question = "Beta question?",
            EventStartsAt = DateTimeOffset.Parse("2026-08-01T10:00:00Z"),
            EventEndsAt = DateTimeOffset.Parse("2026-08-01T12:00:00Z")
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
        var handler = CreateHandler(new InMemoryMarketRepository());

        var result = await handler.Handle(new GetMarketsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Markets.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldExcludeMarketsEndingAtOrBeforeCurrentTime()
    {
        var future = CreateMarket("future", "market-future", "0xfuture", "Future?",
            startsAt: Now.AddMinutes(1), endsAt: Now.AddHours(1));
        var endingNow = CreateMarket("ending-now", "market-ending-now", "0xending", "Ending now?",
            startsAt: Now.AddHours(-1), endsAt: Now);
        var ended = CreateMarket("ended", "market-ended", "0xended", "Ended?",
            startsAt: Now.AddHours(-2), endsAt: Now.AddMinutes(-1));
        var handler = CreateHandler(new InMemoryMarketRepository(
            future,
            endingNow,
            ended));

        var result = await handler.Handle(new GetMarketsQuery(), CancellationToken.None);

        result.Value.Markets.Select(market => market.MarketSlug)
            .Should()
            .Equal("future");
    }

    [Fact]
    public async Task Handle_TradingNow_ShouldReturnOnlyAvailableMarkets()
    {
        var available = CreateMarket("available", "market-available", "0xavailable", "Available?");
        var closed = CreateMarket("closed", "market-closed", "0xclosed", "Closed?");
        var notAcceptingOrders = CreateMarket(
            "not-accepting",
            "market-not-accepting",
            "0xnot-accepting",
            "Not accepting?");
        var expired = CreateMarket(
            "expired",
            "market-expired",
            "0xexpired",
            "Expired?",
            startsAt: Now.AddHours(-2),
            endsAt: Now);
        var gateway = new StubExternalMarketGateway(new Dictionary<string, ExternalMarket>
        {
            ["available"] = CreateExternalMarket("available"),
            ["closed"] = CreateExternalMarket("closed") with { Closed = true },
            ["not-accepting"] = CreateExternalMarket("not-accepting") with
            {
                AcceptingOrders = false
            },
            ["expired"] = CreateExternalMarket("expired")
        });
        var handler = new GetMarketsHandler(
            new InMemoryMarketRepository(available, closed, notAcceptingOrders, expired),
            gateway,
            new FixedTimeProvider(Now));

        var result = await handler.Handle(new GetMarketsQuery(true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Markets.Select(market => market.MarketSlug).Should().Equal("available");
        gateway.RequestedSlugs.Should().Equal("available", "closed", "not-accepting");
    }

    [Fact]
    public async Task Handle_TradingNow_WhenGammaFails_ShouldReturnFailure()
    {
        var market = CreateMarket("market", "market-id", "0xmarket", "Market?");
        var error = new Error(
            "gamma.market.request_failed",
            "Gamma request failed.",
            ErrorType.Failure);
        var handler = new GetMarketsHandler(
            new InMemoryMarketRepository(market),
            new StubExternalMarketGateway(error),
            new FixedTimeProvider(Now));

        var result = await handler.Handle(new GetMarketsQuery(true), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().ContainSingle().Which.Should().Be(error);
    }

    private static GetMarketsHandler CreateHandler(IMarketRepository repository)
    {
        return new GetMarketsHandler(
            repository,
            new StubExternalMarketGateway(new Dictionary<string, ExternalMarket>()),
            new FixedTimeProvider(Now));
    }

    private static ExternalMarket CreateExternalMarket(string slug)
    {
        return new ExternalMarket(
            ExternalMarketId: $"external-{slug}",
            Slug: slug,
            Question: $"Question {slug}?",
            ConditionId: $"condition-{slug}",
            ExternalCreatedAt: null,
            OrdersOpenedAt: null,
            GammaStartDate: null,
            EventStartsAt: DateTimeOffset.Parse("2026-08-01T10:00:00Z"),
            EventEndsAt: DateTimeOffset.Parse("2026-08-01T12:00:00Z"),
            ExternalClosedAt: null,
            UmaResolutionStatus: null,
            Active: true,
            Closed: false,
            AcceptingOrders: true,
            OrderBookEnabled: true,
            Tokens: []);
    }

    private static Market CreateMarket(
        string slug,
        string externalId,
        string conditionId,
        string question,
        DateTimeOffset? startsAt = null,
        DateTimeOffset? endsAt = null)
    {
        var market = Market.Create(
            MarketId.Create(Guid.NewGuid()).Value,
            ExternalEventId.Create($"event-{slug}").Value,
            EventSlug.Create($"event-{slug}").Value,
            ExternalMarketId.Create(externalId).Value,
            MarketSlug.Create(slug).Value,
            ConditionId.Create(conditionId).Value,
            question,
            DateTimeOffset.Parse("2026-07-31T10:00:00Z"),
            null,
            null,
            null,
            startsAt ?? DateTimeOffset.Parse("2026-08-01T10:00:00Z"),
            endsAt ?? DateTimeOffset.Parse("2026-08-01T12:00:00Z"),
            null,
            DateTimeOffset.Parse("2026-07-31T10:00:00Z")).Value;

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

    private sealed class StubExternalMarketGateway : IExternalMarketGateway
    {
        private readonly IReadOnlyDictionary<string, ExternalMarket>? _markets;
        private readonly Error? _error;

        public StubExternalMarketGateway(IReadOnlyDictionary<string, ExternalMarket> markets)
        {
            _markets = markets;
        }

        public StubExternalMarketGateway(Error error)
        {
            _error = error;
        }

        public List<string> RequestedSlugs { get; } = [];

        public Task<Result<ExternalEvent, Error>> GetByEventSlugAsync(
            EventSlug eventSlug,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<ExternalMarket, Error>> GetByMarketSlugAsync(
            MarketSlug slug,
            CancellationToken cancellationToken)
        {
            RequestedSlugs.Add(slug.Value);

            Result<ExternalMarket, Error> result = _error is not null
                ? _error
                : _markets![slug.Value];
            return Task.FromResult(result);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
