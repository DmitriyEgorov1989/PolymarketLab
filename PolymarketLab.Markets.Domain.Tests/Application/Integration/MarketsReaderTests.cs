using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PolymarketLab.Markets.Contracts;
using PolymarketLab.Markets.Core.Application.DependencyInjection;
using PolymarketLab.Markets.Core.Domain.Models.Market.MarketAggregate;
using PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects;
using PolymarketLab.Markets.Core.Ports;
using PolymarketLab.Markets.Core.Ports.Dto;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;

namespace PolymarketLab.Markets.Domain.Tests.Application.Integration;

public sealed class MarketsReaderTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-10T12:00:00Z");

    [Fact]
    public async Task GetForCollectionAsync_WithStoredMarket_ShouldReturnContract()
    {
        var market = CreateMarket();
        var repository = new StubMarketRepository(market);
        var gateway = new StubExternalMarketGateway(CreateExternalMarket());
        using var provider = CreateProvider(repository, gateway);
        var reader = provider.GetRequiredService<IMarketsReader>();

        var result = await reader.GetForCollectionAsync(market.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.MarketId.Should().Be(market.Id);
        result.Value.Slug.Should().Be("will-it-rain");
        result.Value.Tokens.Should().BeEquivalentTo(
        [
            new MarketTokenForCollection(TokenId.Create("token-yes").Value, "Yes", 0),
            new MarketTokenForCollection(TokenId.Create("token-no").Value, "No", 1)
        ]);
    }

    [Fact]
    public async Task GetForCollectionAsync_WithMissingMarket_ShouldReturnNullAndForwardCancellation()
    {
        var repository = new StubMarketRepository(null);
        var gateway = new StubExternalMarketGateway(CreateExternalMarket());
        using var provider = CreateProvider(repository, gateway);
        var reader = provider.GetRequiredService<IMarketsReader>();
        using var cancellationTokenSource = new CancellationTokenSource();

        var result = await reader.GetForCollectionAsync(
            MarketId.Create(Guid.NewGuid()).Value,
            cancellationTokenSource.Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
        repository.LastCancellationToken.Should().Be(cancellationTokenSource.Token);
        gateway.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false, false, true, true)]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, false)]
    public async Task GetForCollectionAsync_WithUnavailableMarket_ShouldReturnConflict(
        bool active,
        bool closed,
        bool acceptingOrders,
        bool orderBookEnabled)
    {
        var market = CreateMarket();
        var gateway = new StubExternalMarketGateway(
            CreateExternalMarket() with
            {
                Active = active,
                Closed = closed,
                AcceptingOrders = acceptingOrders,
                OrderBookEnabled = orderBookEnabled
            });
        using var provider = CreateProvider(new StubMarketRepository(market), gateway);
        var reader = provider.GetRequiredService<IMarketsReader>();

        var result = await reader.GetForCollectionAsync(market.Id, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("market.collection.unavailable");
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task GetForCollectionAsync_WithPastExternalEndDate_ShouldReturnContract()
    {
        var market = CreateMarket();
        var gateway = new StubExternalMarketGateway(CreateExternalMarket() with
        {
            EventStartsAt = Now.AddDays(-2),
            EventEndsAt = Now.AddDays(-1)
        });
        using var provider = CreateProvider(new StubMarketRepository(market), gateway);
        var reader = provider.GetRequiredService<IMarketsReader>();

        var result = await reader.GetForCollectionAsync(market.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task GetForCollectionAsync_WhenGammaFails_ShouldPreserveError()
    {
        var market = CreateMarket();
        var error = new Error("gamma.market.timeout", "Gamma timed out.", ErrorType.Failure);
        using var provider = CreateProvider(
            new StubMarketRepository(market),
            new StubExternalMarketGateway(error));
        var reader = provider.GetRequiredService<IMarketsReader>();

        var result = await reader.GetForCollectionAsync(market.Id, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    private static ServiceProvider CreateProvider(
        IMarketRepository repository,
        IExternalMarketGateway gateway)
    {
        var services = new ServiceCollection();
        services.AddMarketsApplication();
        services.AddSingleton(repository);
        services.AddSingleton(gateway);
        return services.BuildServiceProvider();
    }

    private static ExternalMarket CreateExternalMarket()
    {
        return new ExternalMarket(
            ExternalMarketId: "market-123",
            Slug: "will-it-rain",
            Question: "Will it rain?",
            ConditionId: "0xcondition",
            ExternalCreatedAt: null,
            OrdersOpenedAt: null,
            GammaStartDate: null,
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
            Now,
            null,
            null,
            null,
            Now.AddHours(1),
            Now.AddHours(1).AddMinutes(5),
            null,
            Now).Value;

        market.AddToken(TokenId.Create("token-yes").Value, "Yes", 0);
        market.AddToken(TokenId.Create("token-no").Value, "No", 1);

        return market;
    }

    private sealed class StubMarketRepository(Market? market) : IMarketRepository
    {
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<IReadOnlyCollection<Market>> GetAllAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Market?> GetByIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult(market?.Id.Equals(marketId) == true ? market : null);
        }

        public Task<Market?> GetByEventSlugAsync(
            EventSlug eventSlug,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Market?> GetByExternalEventIdAsync(
            ExternalEventId externalEventId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Market?> GetBySlugAsync(
            MarketSlug slug,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Market?> GetByExternalIdAsync(
            ExternalMarketId externalMarketId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Market?> GetByConditionIdAsync(
            ConditionId conditionId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyCollection<Market>> GetByAnyTokenIdsAsync(
            IReadOnlyCollection<TokenId> tokenIds,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<MarketInsertStatus, Error>> TryAddAsync(
            Market market,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UnitResult<Error>> UpdateScheduleAsync(
            Market market,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubExternalMarketGateway : IExternalMarketGateway
    {
        private readonly Result<ExternalMarket, Error> _result;

        public StubExternalMarketGateway(ExternalMarket market) => _result = market;
        public StubExternalMarketGateway(Error error) => _result = error;

        public int CallCount { get; private set; }

        public Task<Result<ExternalEvent, Error>> GetByEventSlugAsync(
            EventSlug eventSlug,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<ExternalMarket, Error>> GetByMarketSlugAsync(
            MarketSlug slug,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

}
