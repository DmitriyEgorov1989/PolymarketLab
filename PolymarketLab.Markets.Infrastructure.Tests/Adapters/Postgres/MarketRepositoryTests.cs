using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PolymarketLab.Markets.Core.Domain.Models.Market.MarketAggregate;
using PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects;
using PolymarketLab.Markets.Core.Ports;
using PolymarketLab.Markets.Infrastructure.Adapters.Postgres;
using PolymarketLab.Markets.Infrastructure.Adapters.Postgres.Repository;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;

namespace PolymarketLab.Markets.Infrastructure.Tests.Adapters.Postgres;

public sealed class MarketRepositoryTests
{
    [Fact]
    public async Task TryAddAsync_WithValidAggregate_ShouldSaveMarketAndTokens()
    {
        await using var context = CreateContext();
        var repository = new MarketRepository(context);
        var market = CreateMarket();

        var result = await repository.TryAddAsync(market, CancellationToken.None);
        context.ChangeTracker.Clear();
        var storedMarket = await repository.GetBySlugAsync(market.Slug, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(MarketInsertStatus.Inserted);
        storedMarket.Should().NotBeNull();
        storedMarket!.Id.Should().Be(market.Id);
        storedMarket.Tokens.Should().HaveCount(2);
    }

    [Fact]
    public async Task LookupMethods_WithStoredMarket_ShouldFindSameAggregate()
    {
        await using var context = CreateContext();
        var repository = new MarketRepository(context);
        var market = CreateMarket();
        await repository.TryAddAsync(market, CancellationToken.None);
        context.ChangeTracker.Clear();

        var bySlug = await repository.GetBySlugAsync(market.Slug, CancellationToken.None);
        var byExternalId = await repository.GetByExternalIdAsync(
            market.ExternalId,
            CancellationToken.None);
        var byConditionId = await repository.GetByConditionIdAsync(
            market.ConditionId,
            CancellationToken.None);

        bySlug!.Id.Should().Be(market.Id);
        byExternalId!.Id.Should().Be(market.Id);
        byConditionId!.Id.Should().Be(market.Id);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task LookupMethods_WithMissingMarket_ShouldReturnNull()
    {
        await using var context = CreateContext();
        var repository = new MarketRepository(context);

        var bySlug = await repository.GetBySlugAsync(
            MarketSlug.Create("missing").Value,
            CancellationToken.None);
        var byExternalId = await repository.GetByExternalIdAsync(
            ExternalMarketId.Create("missing").Value,
            CancellationToken.None);
        var byConditionId = await repository.GetByConditionIdAsync(
            ConditionId.Create("missing").Value,
            CancellationToken.None);

        bySlug.Should().BeNull();
        byExternalId.Should().BeNull();
        byConditionId.Should().BeNull();
    }

    [Fact]
    public async Task GetBySlugAsync_WithCancelledToken_ShouldPropagateCancellation()
    {
        await using var context = CreateContext();
        var repository = new MarketRepository(context);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        var action = () => repository.GetBySlugAsync(
            MarketSlug.Create("will-it-rain").Value,
            cancellationTokenSource.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private static MarketsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MarketsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MarketsDbContext(options);
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
}
