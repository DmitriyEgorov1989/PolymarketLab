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
        var storedMarket = await repository.GetBySlugAsync(market.MarketSlug, CancellationToken.None);

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

        var byId = await repository.GetByIdAsync(market.Id, CancellationToken.None);
        var byEventSlug = await repository.GetByEventSlugAsync(market.EventSlug, CancellationToken.None);
        var byExternalEventId = await repository.GetByExternalEventIdAsync(
            market.ExternalEventId,
            CancellationToken.None);
        var bySlug = await repository.GetBySlugAsync(market.MarketSlug, CancellationToken.None);
        var byExternalId = await repository.GetByExternalIdAsync(
            market.ExternalMarketId,
            CancellationToken.None);
        var byConditionId = await repository.GetByConditionIdAsync(
            market.ConditionId,
            CancellationToken.None);

        byId!.Id.Should().Be(market.Id);
        byId.Tokens.Should().HaveCount(2);
        byEventSlug!.Id.Should().Be(market.Id);
        byExternalEventId!.Id.Should().Be(market.Id);
        bySlug!.Id.Should().Be(market.Id);
        byExternalId!.Id.Should().Be(market.Id);
        byConditionId!.Id.Should().Be(market.Id);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_WithStoredMarket_ShouldReturnMarketAndTokens()
    {
        await using var context = CreateContext();
        var repository = new MarketRepository(context);
        var market = CreateMarket();
        await repository.TryAddAsync(market, CancellationToken.None);
        context.ChangeTracker.Clear();

        var markets = await repository.GetAllAsync(CancellationToken.None);

        markets.Should().ContainSingle();
        markets.Single().Id.Should().Be(market.Id);
        markets.Single().Tokens.Should().HaveCount(2);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task LookupMethods_WithMissingMarket_ShouldReturnNull()
    {
        await using var context = CreateContext();
        var repository = new MarketRepository(context);

        var byId = await repository.GetByIdAsync(
            MarketId.Create(Guid.NewGuid()).Value,
            CancellationToken.None);
        var bySlug = await repository.GetBySlugAsync(
            MarketSlug.Create("missing").Value,
            CancellationToken.None);
        var byExternalId = await repository.GetByExternalIdAsync(
            ExternalMarketId.Create("missing").Value,
            CancellationToken.None);
        var byConditionId = await repository.GetByConditionIdAsync(
            ConditionId.Create("missing").Value,
            CancellationToken.None);

        byId.Should().BeNull();
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

    private static Market CreateMarket(
        string slug = "will-it-rain",
        string externalId = "market-123",
        string conditionId = "0xcondition")
    {
        var market = Market.Create(
            MarketId.Create(Guid.NewGuid()).Value,
            ExternalEventId.Create($"event-{slug}").Value,
            EventSlug.Create($"event-{slug}").Value,
            ExternalMarketId.Create(externalId).Value,
            MarketSlug.Create(slug).Value,
            ConditionId.Create(conditionId).Value,
            "Will it rain?",
            DateTimeOffset.Parse("2026-07-31T10:00:00Z"),
            null,
            null,
            null,
            DateTimeOffset.Parse("2026-08-01T10:00:00Z"),
            DateTimeOffset.Parse("2026-08-01T10:05:00Z"),
            null,
            DateTimeOffset.Parse("2026-07-31T10:00:00Z")).Value;

        market.AddToken(TokenId.Create("token-yes").Value, "Yes", 0);
        market.AddToken(TokenId.Create("token-no").Value, "No", 1);

        return market;
    }
}
