using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Infrastructure.DependencyInjection;
using PolymarketLab.Markets.Contracts;
using PolymarketLab.SharedKernel.DomainModels.Ids;
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

        result.Should().NotBeNull();
        result!.MarketId.Should().Be(marketId);
        result.Slug.Should().Be("will-it-rain");
        result.Tokens.Should().ContainSingle();
        result.Tokens.Single().TokenId.Should().Be(tokenId);
        result.Tokens.Single().Outcome.Should().Be("Yes");
        result.Tokens.Single().OutcomeIndex.Should().Be(0);
    }

    [Fact]
    public async Task GetByIdAsync_WithMissingMarket_ShouldReturnNullAndForwardCancellation()
    {
        var reader = new StubMarketsReader(null);
        using var provider = CreateProvider(reader);
        var source = provider.GetRequiredService<IMarketCollectionSource>();
        using var cancellationTokenSource = new CancellationTokenSource();

        var result = await source.GetByIdAsync(
            MarketId.Create(Guid.NewGuid()).Value,
            cancellationTokenSource.Token);

        result.Should().BeNull();
        reader.LastCancellationToken.Should().Be(cancellationTokenSource.Token);
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

    private sealed class StubMarketsReader(MarketForCollection? market) : IMarketsReader
    {
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<MarketForCollection?> GetForCollectionAsync(
            MarketId marketId,
            CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult(market?.MarketId.Equals(marketId) == true ? market : null);
        }
    }
}
