using CSharpFunctionalExtensions;
using FluentAssertions;
using PolymarketLab.Markets.Core.Application.UseCases.Commands;
using PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects;
using PolymarketLab.Markets.Core.Ports;
using PolymarketLab.Markets.Core.Ports.Dto;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;
using MarketAggregate = PolymarketLab.Markets.Core.Domain.Models.Market.MarketAggregate.Market;

namespace PolymarketLab.Markets.Domain.Tests.Application.UseCases.Commands;

public sealed class RegisterMarketHandlerTests
{
    private const string MarketUrl = "https://polymarket.com/event/rain-event";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-27T12:00:00Z");
    [Fact]
    public async Task Handle_WithInvalidUrl_ShouldReturnParserErrorWithoutCallingGateway()
    {
        var gateway = new StubExternalMarketGateway(CreateExternalMarket());
        var repository = new InMemoryMarketRepository();
        var handler = CreateHandler(gateway, repository);

        var result = await handler.Handle(new RegisterMarketCommand("not-a-url"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Code.Should().Be("polymarket.url.invalid");
        gateway.CallCount.Should().Be(0);
        repository.TryAddCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithExistingChildMarket_ShouldResolveEventAndReturnExistingMarket()
    {
        var existing = CreateMarket();
        var gateway = new StubExternalMarketGateway(CreateExternalMarket());
        var repository = new InMemoryMarketRepository(existing);
        var handler = CreateHandler(gateway, repository);

        var result = await handler.Handle(new RegisterMarketCommand(MarketUrl), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new RegisterMarketResponse(existing.Id.Value, false));
        gateway.CallCount.Should().Be(1);
        repository.TryAddCallCount.Should().Be(0);
        repository.UpdateScheduleCallCount.Should().Be(1);
        existing.DiscoveredAt.Should().Be(Now.AddDays(-2));
        existing.ScheduleRefreshedAt.Should().Be(Now);
    }

    [Fact]
    public async Task Handle_WithExistingUnavailableChildMarket_ShouldReturnExistingMarket()
    {
        var existing = CreateMarket();
        var externalMarket = CreateExternalMarket() with
        {
            Closed = true,
            AcceptingOrders = false,
            OrderBookEnabled = false
        };
        var repository = new InMemoryMarketRepository(existing);
        var handler = CreateHandler(new StubExternalMarketGateway(externalMarket), repository);

        var result = await handler.Handle(new RegisterMarketCommand(MarketUrl), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new RegisterMarketResponse(existing.Id.Value, false));
        repository.TryAddCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WhenGatewayFails_ShouldReturnGatewayError()
    {
        var gatewayError = new Error("gamma.failed", "Gamma failed.", ErrorType.Failure);
        var gateway = new StubExternalMarketGateway(gatewayError);
        var repository = new InMemoryMarketRepository();
        var handler = CreateHandler(gateway, repository);

        var result = await handler.Handle(new RegisterMarketCommand(MarketUrl), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Should().Be(gatewayError);
        repository.TryAddCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithMissingQuestion_ShouldReturnRequiredError()
    {
        var externalMarket = CreateExternalMarket() with { Question = " " };
        var repository = new InMemoryMarketRepository();
        var handler = CreateHandler(new StubExternalMarketGateway(externalMarket), repository);

        var result = await handler.Handle(new RegisterMarketCommand(MarketUrl), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Type.Should().Be(ErrorType.ValueIsRequired);
        repository.TryAddCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithMismatchedEventSlug_ShouldReturnConflict()
    {
        var externalEvent = CreateExternalEvent() with { Slug = "different-event" };
        var handler = CreateHandler(
            new StubExternalMarketGateway(externalEvent),
            new InMemoryMarketRepository());

        var result = await handler.Handle(new RegisterMarketCommand(MarketUrl), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Code.Should().Be("market.registration.event_slug_mismatch");
    }

    [Fact]
    public async Task Handle_WithDisabledOrderBook_ShouldReturnConflict()
    {
        var externalMarket = CreateExternalMarket() with { OrderBookEnabled = false };
        var handler = CreateHandler(
            new StubExternalMarketGateway(externalMarket),
            new InMemoryMarketRepository());

        var result = await handler.Handle(new RegisterMarketCommand(MarketUrl), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Code.Should().Be("market.registration.order_book_disabled");
    }

    [Fact]
    public async Task Handle_WithFutureMarketNotAcceptingOrders_ShouldRegisterMarket()
    {
        var externalMarket = CreateExternalMarket() with { AcceptingOrders = false };
        var repository = new InMemoryMarketRepository();
        var handler = CreateHandler(new StubExternalMarketGateway(externalMarket), repository);

        var result = await handler.Handle(new RegisterMarketCommand(MarketUrl), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Created.Should().BeTrue();
        repository.TryAddCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WithClosedNewMarket_ShouldReturnConflict()
    {
        var externalMarket = CreateExternalMarket() with { Closed = true };
        var repository = new InMemoryMarketRepository();
        var handler = CreateHandler(new StubExternalMarketGateway(externalMarket), repository);

        var result = await handler.Handle(new RegisterMarketCommand(MarketUrl), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Code.Should().Be("market.registration.unavailable");
        repository.TryAddCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithResolvedNewMarket_ShouldReturnConflict()
    {
        var externalMarket = CreateExternalMarket() with { UmaResolutionStatus = "Resolved" };
        var repository = new InMemoryMarketRepository();
        var handler = CreateHandler(new StubExternalMarketGateway(externalMarket), repository);

        var result = await handler.Handle(new RegisterMarketCommand(MarketUrl), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Code.Should().Be("market.registration.unavailable");
        repository.TryAddCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithExternalClosedTime_ShouldReturnConflict()
    {
        var externalMarket = CreateExternalMarket() with { ExternalClosedAt = Now };
        var repository = new InMemoryMarketRepository();
        var handler = CreateHandler(new StubExternalMarketGateway(externalMarket), repository);

        var result = await handler.Handle(new RegisterMarketCommand(MarketUrl), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Code.Should().Be("market.registration.unavailable");
        repository.TryAddCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithoutTokens_ShouldReturnRequiredError()
    {
        var externalMarket = CreateExternalMarket() with { Tokens = [] };
        var handler = CreateHandler(
            new StubExternalMarketGateway(externalMarket),
            new InMemoryMarketRepository());

        var result = await handler.Handle(new RegisterMarketCommand(MarketUrl), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Code.Should().Be("market.registration.tokens_required");
    }

    [Fact]
    public async Task Handle_WithInvalidTokenId_ShouldReturnValueError()
    {
        var externalMarket = CreateExternalMarket() with
        {
            Tokens = [new ExternalMarketToken("Yes", " ", 0)]
        };
        var repository = new InMemoryMarketRepository();
        var handler = CreateHandler(new StubExternalMarketGateway(externalMarket), repository);

        var result = await handler.Handle(new RegisterMarketCommand(MarketUrl), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Type.Should().Be(ErrorType.ValueIsInvalid);
        repository.TryAddCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithValidMarket_ShouldCreateAggregateAndReturnCreatedResponse()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var gateway = new StubExternalMarketGateway(CreateExternalMarket());
        var repository = new InMemoryMarketRepository();
        var handler = CreateHandler(gateway, repository);

        var result = await handler.Handle(
            new RegisterMarketCommand(MarketUrl),
            cancellationTokenSource.Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.Created.Should().BeTrue();
        result.Value.MarketId.Should().NotBeEmpty();
        repository.Markets.Should().ContainSingle();
        repository.Markets.Single().EventSlug.Value.Should().Be("rain-event");
        repository.Markets.Single().MarketSlug.Value.Should().Be("will-it-rain");
        repository.Markets.Single().DiscoveredAt.Should().Be(Now);
        repository.Markets.Single().Tokens
            .Select(token => (token.ExternalTokenId.Value, token.Outcome, token.OutcomeIndex))
            .Should().Equal(
                ("token-yes", "Yes", 0),
                ("token-no", "No", 1));
        gateway.LastEventSlug.Should().Be(EventSlug.Create("rain-event").Value);
        gateway.LastCancellationToken.Should().Be(cancellationTokenSource.Token);
        repository.LastCancellationToken.Should().Be(cancellationTokenSource.Token);
    }

    [Fact]
    public async Task Handle_WhenIdentityKeysBelongToDifferentMarkets_ShouldReturnConflict()
    {
        var byExternalId = CreateMarket(
            slug: "other-market-a",
            externalId: "market-123",
            conditionId: "0xother-a",
            eventId: "event-other-a",
            eventSlug: "event-other-a");
        var byConditionId = CreateMarket(
            slug: "other-market-b",
            externalId: "market-other-b",
            conditionId: "0xcondition",
            eventId: "event-other-b",
            eventSlug: "event-other-b");
        var repository = new InMemoryMarketRepository(byExternalId, byConditionId);
        var handler = CreateHandler(new StubExternalMarketGateway(CreateExternalMarket()), repository);

        var result = await handler.Handle(new RegisterMarketCommand(MarketUrl), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Code.Should().Be("market.registration.identity_conflict");
        repository.TryAddCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithPartialEventIdentityMatch_ShouldReturnConflict()
    {
        var existing = CreateMarket(eventSlug: "old-rain-event");
        var repository = new InMemoryMarketRepository(existing);
        var handler = CreateHandler(new StubExternalMarketGateway(CreateExternalMarket()), repository);

        var result = await handler.Handle(new RegisterMarketCommand(MarketUrl), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Code.Should().Be("market.registration.identity_conflict");
        repository.UpdateScheduleCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithChangedTokenOutcome_ShouldReturnConflict()
    {
        var externalMarket = CreateExternalMarket() with
        {
            Tokens =
            [
                new ExternalMarketToken("Up", "token-yes", 0),
                new ExternalMarketToken("No", "token-no", 1)
            ]
        };
        var repository = new InMemoryMarketRepository(CreateMarket(eventSlug: "existing-event"));
        var handler = CreateHandler(new StubExternalMarketGateway(externalMarket), repository);

        var result = await handler.Handle(new RegisterMarketCommand(MarketUrl), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Code.Should().Be("market.registration.identity_conflict");
        repository.UpdateScheduleCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithMatchingTokenIdsAndDifferentMarketKeys_ShouldReturnConflict()
    {
        var externalEvent = CreateExternalEvent(CreateExternalMarket() with
        {
            ExternalMarketId = "market-other",
            Slug = "market-other",
            ConditionId = "condition-other"
        }) with
        {
            ExternalEventId = "event-other"
        };
        var repository = new InMemoryMarketRepository(CreateMarket(eventSlug: "existing-event"));
        var handler = CreateHandler(new StubExternalMarketGateway(externalEvent), repository);

        var result = await handler.Handle(new RegisterMarketCommand(MarketUrl), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Code.Should().Be("market.registration.identity_conflict");
        repository.TryAddCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WhenInsertLosesRace_ShouldReturnPersistedMarket()
    {
        var persistedMarket = CreateMarket();
        var repository = new InMemoryMarketRepository();
        repository.TryAddHandler = _ =>
        {
            repository.AddPersisted(persistedMarket);
            return MarketInsertStatus.UniqueConflict;
        };
        var handler = CreateHandler(new StubExternalMarketGateway(CreateExternalMarket()), repository);

        var result = await handler.Handle(new RegisterMarketCommand(MarketUrl), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new RegisterMarketResponse(persistedMarket.Id.Value, false));
        repository.TryAddCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WhenInsertRaceCannotBeResolved_ShouldReturnConflict()
    {
        var repository = new InMemoryMarketRepository
        {
            TryAddHandler = _ => MarketInsertStatus.UniqueConflict
        };
        var handler = CreateHandler(new StubExternalMarketGateway(CreateExternalMarket()), repository);

        var result = await handler.Handle(new RegisterMarketCommand(MarketUrl), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Code.Should().Be("market.registration.race_unresolved");
    }

    private static RegisterMarketHandler CreateHandler(
        IExternalMarketGateway gateway,
        IMarketRepository repository)
    {
        return new RegisterMarketHandler(gateway, repository, new FixedTimeProvider(Now));
    }

    private static ExternalMarket CreateExternalMarket()
    {
        return new ExternalMarket(
            ExternalMarketId: "market-123",
            Slug: "will-it-rain",
            Question: "Will it rain?",
            ConditionId: "0xcondition",
            ExternalCreatedAt: Now.AddDays(-1),
            OrdersOpenedAt: null,
            GammaStartDate: Now.AddMinutes(30),
            EventStartsAt: Now.AddHours(1),
            EventEndsAt: Now.AddHours(1).AddMinutes(5),
            ExternalClosedAt: null,
            UmaResolutionStatus: null,
            Active: true,
            Closed: false,
            AcceptingOrders: true,
            OrderBookEnabled: true,
            Tokens:
            [
                new ExternalMarketToken("Yes", "token-yes", 0),
                new ExternalMarketToken("No", "token-no", 1)
            ]);
    }

    private static ExternalEvent CreateExternalEvent(ExternalMarket? market = null)
    {
        return new ExternalEvent("event-123", "rain-event", market ?? CreateExternalMarket());
    }

    private static MarketAggregate CreateMarket(
        string slug = "will-it-rain",
        string externalId = "market-123",
        string conditionId = "0xcondition",
        string eventId = "event-123",
        string eventSlug = "rain-event")
    {
        var market = MarketAggregate.Create(
            MarketId.Create(Guid.NewGuid()).Value,
            ExternalEventId.Create(eventId).Value,
            EventSlug.Create(eventSlug).Value,
            ExternalMarketId.Create(externalId).Value,
            MarketSlug.Create(slug).Value,
            ConditionId.Create(conditionId).Value,
            "Will it rain?",
            Now.AddDays(-2),
            Now.AddDays(-1),
            null,
            Now.AddMinutes(30),
            Now.AddHours(1),
            Now.AddHours(1).AddMinutes(5),
            null,
            Now.AddDays(-2)).Value;

        market.AddToken(TokenId.Create("token-yes").Value, "Yes", 0);
        market.AddToken(TokenId.Create("token-no").Value, "No", 1);
        return market;
    }

    private sealed class StubExternalMarketGateway : IExternalMarketGateway
    {
        private readonly Result<ExternalEvent, Error> _result;

        public StubExternalMarketGateway(ExternalMarket market)
        {
            _result = CreateExternalEvent(market);
        }

        public StubExternalMarketGateway(ExternalEvent externalEvent)
        {
            _result = externalEvent;
        }

        public StubExternalMarketGateway(Error error)
        {
            _result = error;
        }

        public int CallCount { get; private set; }
        public EventSlug? LastEventSlug { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<Result<ExternalEvent, Error>> GetByEventSlugAsync(
            EventSlug eventSlug,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastEventSlug = eventSlug;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(_result);
        }

        public Task<Result<ExternalMarket, Error>> GetByMarketSlugAsync(
            MarketSlug slug,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class InMemoryMarketRepository(params MarketAggregate[] markets) : IMarketRepository
    {
        private readonly List<MarketAggregate> _markets = [.. markets];

        public IReadOnlyCollection<MarketAggregate> Markets => _markets;
        public int TryAddCallCount { get; private set; }
        public int UpdateScheduleCallCount { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }
        public Func<MarketAggregate, Result<MarketInsertStatus, Error>>? TryAddHandler { get; set; }

        public Task<MarketAggregate?> GetByIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult(_markets.SingleOrDefault(market => market.Id.Equals(marketId)));
        }

        public Task<IReadOnlyCollection<MarketAggregate>> GetAllAsync(
            CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult<IReadOnlyCollection<MarketAggregate>>(_markets.ToArray());
        }

        public Task<MarketAggregate?> GetByEventSlugAsync(
            EventSlug eventSlug,
            CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult(_markets.SingleOrDefault(market => market.EventSlug.Equals(eventSlug)));
        }

        public Task<MarketAggregate?> GetByExternalEventIdAsync(
            ExternalEventId externalEventId,
            CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult(
                _markets.SingleOrDefault(market => market.ExternalEventId.Equals(externalEventId)));
        }

        public Task<MarketAggregate?> GetBySlugAsync(
            MarketSlug slug,
            CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult(_markets.SingleOrDefault(market => market.MarketSlug.Equals(slug)));
        }

        public Task<MarketAggregate?> GetByExternalIdAsync(
            ExternalMarketId externalMarketId,
            CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult(
                _markets.SingleOrDefault(market => market.ExternalMarketId.Equals(externalMarketId)));
        }

        public Task<MarketAggregate?> GetByConditionIdAsync(
            ConditionId conditionId,
            CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult(
                _markets.SingleOrDefault(market => market.ConditionId.Equals(conditionId)));
        }

        public Task<IReadOnlyCollection<MarketAggregate>> GetByAnyTokenIdsAsync(
            IReadOnlyCollection<TokenId> tokenIds,
            CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult<IReadOnlyCollection<MarketAggregate>>(
                _markets.Where(market => market.Tokens.Any(token =>
                        tokenIds.Contains(token.ExternalTokenId)))
                    .ToArray());
        }

        public Task<Result<MarketInsertStatus, Error>> TryAddAsync(
            MarketAggregate market,
            CancellationToken cancellationToken)
        {
            TryAddCallCount++;
            LastCancellationToken = cancellationToken;

            if (TryAddHandler is not null)
                return Task.FromResult(TryAddHandler(market));

            _markets.Add(market);
            return Task.FromResult<Result<MarketInsertStatus, Error>>(MarketInsertStatus.Inserted);
        }

        public void AddPersisted(MarketAggregate market)
        {
            _markets.Add(market);
        }

        public Task<UnitResult<Error>> UpdateScheduleAsync(
            MarketAggregate market,
            CancellationToken cancellationToken)
        {
            UpdateScheduleCallCount++;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(UnitResult.Success<Error>());
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
