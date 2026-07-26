using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PolymarketLab.Markets.Contracts;
using PolymarketLab.Markets.Core.Application.DependencyInjection;
using PolymarketLab.Markets.Core.Domain.Models.Market.MarketAggregate;
using PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects;
using PolymarketLab.Markets.Core.Ports;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;

namespace PolymarketLab.Markets.Domain.Tests.Application.Integration;

public sealed class MarketsReaderTests
{
    [Fact]
    public async Task GetForCollectionAsync_WithStoredMarket_ShouldReturnContract()
    {
        var market = CreateMarket();
        var repository = new StubMarketRepository(market);
        using var provider = CreateProvider(repository);
        var reader = provider.GetRequiredService<IMarketsReader>();

        var result = await reader.GetForCollectionAsync(market.Id, CancellationToken.None);

        result.Should().NotBeNull();
        result!.MarketId.Should().Be(market.Id);
        result.Slug.Should().Be("will-it-rain");
        result.Tokens.Should().BeEquivalentTo(
        [
            new MarketTokenForCollection(TokenId.Create("token-yes").Value, "Yes", 0),
            new MarketTokenForCollection(TokenId.Create("token-no").Value, "No", 1)
        ]);
    }

    [Fact]
    public async Task GetForCollectionAsync_WithMissingMarket_ShouldReturnNullAndForwardCancellation()
    {
        var repository = new StubMarketRepository(null);
        using var provider = CreateProvider(repository);
        var reader = provider.GetRequiredService<IMarketsReader>();
        using var cancellationTokenSource = new CancellationTokenSource();

        var result = await reader.GetForCollectionAsync(
            MarketId.Create(Guid.NewGuid()).Value,
            cancellationTokenSource.Token);

        result.Should().BeNull();
        repository.LastCancellationToken.Should().Be(cancellationTokenSource.Token);
    }

    private static ServiceProvider CreateProvider(IMarketRepository repository)
    {
        var services = new ServiceCollection();
        services.AddMarketsApplication();
        services.AddSingleton(repository);
        return services.BuildServiceProvider();
    }

    private static Market CreateMarket()
    {
        var market = Market.Create(
            MarketId.Create(Guid.NewGuid()).Value,
            ExternalMarketId.Create("market-123").Value,
            MarketSlug.Create("will-it-rain").Value,
            ConditionId.Create("0xcondition").Value,
            "Will it rain?",
            null,
            null).Value;

        market.AddToken(TokenId.Create("token-yes").Value, "Yes", 0);
        market.AddToken(TokenId.Create("token-no").Value, "No", 1);

        return market;
    }

    private sealed class StubMarketRepository(Market? market) : IMarketRepository
    {
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<Market?> GetByIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult(market?.Id.Equals(marketId) == true ? market : null);
        }

        public Task<Market?> GetBySlugAsync(
            MarketSlug slug,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Market?> GetByExternalIdAsync(
            ExternalMarketId externalMarketId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Market?> GetByConditionIdAsync(
            ConditionId conditionId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<MarketInsertStatus, Error>> TryAddAsync(
            Market market,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
