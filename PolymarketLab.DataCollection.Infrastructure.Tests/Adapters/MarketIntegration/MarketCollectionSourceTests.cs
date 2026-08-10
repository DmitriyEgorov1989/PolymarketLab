using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Infrastructure.DependencyInjection;
using PolymarketLab.Markets.Contracts;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.MarketIntegration;

public sealed class MarketCollectionSourceTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=polymarket_lab;Username=postgres;Password=postgres";

    [Fact]
    public async Task GetByIdAsync_WithMarketContract_ShouldMapCollectionMarket()
    {
        var marketId = MarketId.Create(Guid.NewGuid()).Value;
        var tokenId = TokenId.Create("token-yes").Value;
        var reader = new StubMarketsReader(new MarketForCollection(
            marketId,
            "will-it-rain",
            [new MarketTokenForCollection(tokenId, "Yes", 0)]));
        using var provider = CreateProvider(reader);
        var source = provider.GetRequiredService<IMarketCollectionSource>();

        var result = await source.GetByIdAsync(marketId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.MarketId.Should().Be(marketId);
        result.Value.Slug.Should().Be("will-it-rain");
        result.Value.Tokens.Should().ContainSingle();
        result.Value.Tokens.Single().TokenId.Should().Be(tokenId);
        result.Value.Tokens.Single().Outcome.Should().Be("Yes");
        result.Value.Tokens.Single().OutcomeIndex.Should().Be(0);
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

        public StubMarketsReader(MarketForCollection? market) => _result = market;
        public StubMarketsReader(Error error) => _result = error;

        public CancellationToken LastCancellationToken { get; private set; }

        public Task<Result<MarketForCollection?, Error>> GetForCollectionAsync(
            MarketId marketId,
            CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;
            if (_result.IsFailure)
                return Task.FromResult(_result);

            var market = _result.Value;
            return Task.FromResult<Result<MarketForCollection?, Error>>(
                market?.MarketId.Equals(marketId) == true ? market : null);
        }
    }
}
