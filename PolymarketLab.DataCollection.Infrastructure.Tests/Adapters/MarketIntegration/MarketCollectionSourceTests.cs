using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.DependencyInjection;
using PolymarketLab.Markets.Contracts;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.MarketIntegration;

public sealed class MarketCollectionSourceTests
{
    private static readonly DateTimeOffset StartsAt = DateTimeOffset.Parse("2026-08-28T12:00:00Z");
    private static readonly DateTimeOffset EndsAt = StartsAt.AddMinutes(5);
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=polymarket_lab;Username=postgres;Password=postgres";

    [Fact]
    public async Task GetWindowAsync_WithStoredWindow_ShouldMapWithoutFreshMarketRead()
    {
        var marketId = MarketId.Create(Guid.NewGuid()).Value;
        var reader = new StubMarketsReader(new MarketCollectionWindow(marketId, StartsAt));
        using var provider = CreateProvider(reader);
        var source = provider.GetRequiredService<IMarketCollectionSource>();

        var result = await source.GetWindowAsync(marketId, CancellationToken.None);

        result.Should().Be(new CollectionMarketWindow(marketId, StartsAt));
        reader.FreshReadCallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetByIdAsync_WithMarketContract_ShouldMapCollectionMarket()
    {
        var marketId = MarketId.Create(Guid.NewGuid()).Value;
        var yesTokenId = TokenId.Create("token-yes").Value;
        var noTokenId = TokenId.Create("token-no").Value;
        var reader = new StubMarketsReader(new MarketForCollection(
            marketId,
            "event-123",
            "rain-event",
            "market-123",
            "will-it-rain",
            "0xcondition",
            StartsAt,
            EndsAt,
            false,
            false,
            false,
            true,
            [
                new MarketTokenForCollection(yesTokenId, "Yes", 0),
                new MarketTokenForCollection(noTokenId, "No", 1)
            ]));
        using var provider = CreateProvider(reader);
        var source = provider.GetRequiredService<IMarketCollectionSource>();

        var result = await source.GetByIdAsync(marketId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.MarketId.Should().Be(marketId);
        result.Value.ExternalEventId.Should().Be("event-123");
        result.Value.EventSlug.Should().Be("rain-event");
        result.Value.ExternalMarketId.Should().Be("market-123");
        result.Value.MarketSlug.Should().Be("will-it-rain");
        result.Value.ConditionId.Should().Be("0xcondition");
        result.Value.EventStartsAt.Should().Be(StartsAt);
        result.Value.EventEndsAt.Should().Be(EndsAt);
        result.Value.Active.Should().BeFalse();
        result.Value.Closed.Should().BeFalse();
        result.Value.AcceptingOrders.Should().BeFalse();
        result.Value.OrderBookEnabled.Should().BeTrue();
        result.Value.Tokens.Should().Equal(
            new CollectionMarketToken(yesTokenId, "Yes", 0),
            new CollectionMarketToken(noTokenId, "No", 1));
    }

    [Fact]
    public async Task GetByIdAsync_WithMissingMarket_ShouldReturnNullAndForwardCancellation()
    {
        var reader = new StubMarketsReader((MarketForCollection?)null);
        using var provider = CreateProvider(reader);
        var source = provider.GetRequiredService<IMarketCollectionSource>();
        using var cancellationTokenSource = new CancellationTokenSource();

        var result = await source.GetByIdAsync(
            MarketId.Create(Guid.NewGuid()).Value,
            cancellationTokenSource.Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
        reader.LastCancellationToken.Should().Be(cancellationTokenSource.Token);
    }

    [Fact]
    public async Task GetByIdAsync_WhenMarketsReaderFails_ShouldPreserveError()
    {
        var error = new Error("gamma.market.timeout", "Gamma timed out.", ErrorType.Failure);
        var reader = new StubMarketsReader(error);
        using var provider = CreateProvider(reader);
        var source = provider.GetRequiredService<IMarketCollectionSource>();

        var result = await source.GetByIdAsync(
            MarketId.Create(Guid.NewGuid()).Value,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    private static ServiceProvider CreateProvider(IMarketsReader reader)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = ConnectionString
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton(reader);
        services.AddDataCollectionInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    private sealed class StubMarketsReader : IMarketsReader
    {
        private readonly Result<MarketForCollection?, Error> _result;
        private readonly MarketCollectionWindow? _window;

        public StubMarketsReader(MarketForCollection? market) => _result = market;
        public StubMarketsReader(Error error) => _result = error;
        public StubMarketsReader(MarketCollectionWindow window)
        {
            _window = window;
            _result = (MarketForCollection?)null;
        }

        public CancellationToken LastCancellationToken { get; private set; }
        public int FreshReadCallCount { get; private set; }

        public Task<MarketCollectionWindow?> GetCollectionWindowAsync(
            MarketId marketId,
            CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult(
                _window?.MarketId == marketId ? _window : null);
        }

        public Task<Result<MarketForCollection?, Error>> GetForCollectionAsync(
            MarketId marketId,
            CancellationToken cancellationToken)
        {
            FreshReadCallCount++;
            LastCancellationToken = cancellationToken;
            if (_result.IsFailure)
                return Task.FromResult(_result);

            var market = _result.Value;
            return Task.FromResult<Result<MarketForCollection?, Error>>(
                market?.MarketId.Equals(marketId) == true ? market : null);
        }
    }
}
