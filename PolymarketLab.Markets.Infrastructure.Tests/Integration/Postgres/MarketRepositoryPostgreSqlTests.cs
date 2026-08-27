using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using PolymarketLab.Markets.Core.Application.UseCases.Commands;
using PolymarketLab.Markets.Core.Domain.Models.Market.MarketAggregate;
using PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects;
using PolymarketLab.Markets.Core.Ports;
using PolymarketLab.Markets.Core.Ports.Dto;
using PolymarketLab.Markets.Infrastructure.Adapters.Postgres;
using PolymarketLab.Markets.Infrastructure.Adapters.Postgres.Repository;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;

namespace PolymarketLab.Markets.Infrastructure.Tests.Integration.Postgres;

[Collection(PostgreSqlCollection.Name)]
public sealed class MarketRepositoryPostgreSqlTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task MigrationsAndRepository_ShouldRoundTripUtcScheduleAndTokens()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = CreateContext(database.ConnectionString);
        await context.Database.MigrateAsync();
        var repository = new MarketRepository(context);
        var market = CreateMarket();

        var insertResult = await repository.TryAddAsync(market, CancellationToken.None);
        context.ChangeTracker.Clear();
        var stored = await repository.GetByIdAsync(market.Id, CancellationToken.None);
        var stale = await repository.GetByIdAsync(market.Id, CancellationToken.None);

        insertResult.Value.Should().Be(MarketInsertStatus.Inserted);
        stored.Should().NotBeNull();
        stored!.DiscoveredAt.Offset.Should().Be(TimeSpan.Zero);
        stored.ExternalCreatedAt!.Value.Offset.Should().Be(TimeSpan.Zero);
        stored.EventStartsAt.Offset.Should().Be(TimeSpan.Zero);
        stored.EventEndsAt.Offset.Should().Be(TimeSpan.Zero);
        stored.Tokens.OrderBy(token => token.OutcomeIndex)
            .Select(token => (token.ExternalTokenId.Value, token.Outcome, token.OutcomeIndex))
            .Should().Equal(("token-yes", "Yes", 0), ("token-no", "No", 1));

        var refreshedAt = DateTimeOffset.Parse("2026-08-27T15:00:00+03:00");
        stored.RefreshSchedule(
            null,
            refreshedAt.AddMinutes(-30),
            refreshedAt.AddMinutes(-10),
            refreshedAt.AddHours(1),
            refreshedAt.AddHours(1).AddMinutes(5),
            null,
            refreshedAt);
        await repository.UpdateScheduleAsync(stored, CancellationToken.None);
        stale!.RefreshSchedule(
            null,
            null,
            null,
            refreshedAt.AddMinutes(30),
            refreshedAt.AddMinutes(35),
            null,
            refreshedAt.AddMinutes(-1));
        await repository.UpdateScheduleAsync(stale, CancellationToken.None);
        var refreshed = await repository.GetByIdAsync(market.Id, CancellationToken.None);

        refreshed!.DiscoveredAt.Should().Be(market.DiscoveredAt);
        refreshed.ExternalCreatedAt.Should().BeNull();
        refreshed.EventStartsAt.Should().Be(refreshedAt.AddHours(1).ToUniversalTime());
        refreshed.ScheduleRefreshedAt.Should().Be(refreshedAt.ToUniversalTime());
    }

    [Theory]
    [InlineData("external-event-id")]
    [InlineData("event-slug")]
    [InlineData("external-market-id")]
    [InlineData("market-slug")]
    [InlineData("condition-id")]
    [InlineData("external-token-id")]
    public async Task TryAddAsync_WithDuplicateIdentityConstraint_ShouldReturnUniqueConflict(string key)
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = CreateContext(database.ConnectionString);
        await context.Database.MigrateAsync();
        var repository = new MarketRepository(context);
        var existing = CreateMarket();
        await repository.TryAddAsync(existing, CancellationToken.None);
        var duplicate = CreateMarket(key);

        var result = await repository.TryAddAsync(duplicate, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(MarketInsertStatus.UniqueConflict);
        duplicate.Tokens.Should().HaveCount(2);
    }

    [Fact]
    public async Task Migration_WithExistingMarket_ShouldRequireEmptyMarketsTable()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = CreateContext(database.ConnectionString);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20260721122759_InitialMarkets");
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO markets (id, external_market_id, slug, condition_id, question)
            VALUES ('11111111-1111-1111-1111-111111111111', 'market-legacy', 'legacy', 'condition-legacy', 'Legacy?')
            """);

        var action = () => migrator.MigrateAsync();

        var exception = await action.Should().ThrowAsync<PostgresException>();
        exception.Which.MessageText.Should().Contain("requires an empty markets table");
    }

    [Fact]
    public async Task Registration_WithPersistedMarket_ShouldUseCompleteIdentityAndRefreshSchedule()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = CreateContext(database.ConnectionString);
        await context.Database.MigrateAsync();
        var repository = new MarketRepository(context);
        var existing = CreateMarket();
        await repository.TryAddAsync(existing, CancellationToken.None);
        context.ChangeTracker.Clear();
        var now = DateTimeOffset.Parse("2026-08-27T12:00:00Z");

        var exactHandler = new RegisterMarketHandler(
            new StubExternalMarketGateway(CreateExternalEvent(scheduleOffsetMinutes: 5)),
            repository,
            new FixedTimeProvider(now));
        var exact = await exactHandler.Handle(
            new RegisterMarketCommand("https://polymarket.com/event/rain-event"),
            CancellationToken.None);
        var refreshed = await repository.GetByIdAsync(existing.Id, CancellationToken.None);

        exact.IsSuccess.Should().BeTrue();
        exact.Value.Should().Be(new RegisterMarketResponse(existing.Id.Value, false));
        refreshed!.DiscoveredAt.Should().Be(existing.DiscoveredAt);
        refreshed.EventStartsAt.Should().Be(now.AddHours(1).AddMinutes(5));
        refreshed.ScheduleRefreshedAt.Should().Be(now);

        var partialHandler = new RegisterMarketHandler(
            new StubExternalMarketGateway(CreateExternalEvent(externalEventId: "event-other")),
            repository,
            new FixedTimeProvider(now.AddMinutes(1)));
        var partial = await partialHandler.Handle(
            new RegisterMarketCommand("https://polymarket.com/event/rain-event"),
            CancellationToken.None);

        partial.IsFailure.Should().BeTrue();
        partial.Error.Single().Code.Should().Be("market.registration.identity_conflict");

        var tokenHandler = new RegisterMarketHandler(
            new StubExternalMarketGateway(CreateExternalEvent(firstOutcome: "Up")),
            repository,
            new FixedTimeProvider(now.AddMinutes(2)));
        var tokenConflict = await tokenHandler.Handle(
            new RegisterMarketCommand("https://polymarket.com/event/rain-event"),
            CancellationToken.None);

        tokenConflict.IsFailure.Should().BeTrue();
        tokenConflict.Error.Single().Code.Should().Be("market.registration.identity_conflict");

        var tokenIdHandler = new RegisterMarketHandler(
            new StubExternalMarketGateway(CreateExternalEvent(tokens:
            [
                new ExternalMarketToken("Yes", "token-updated", 0),
                new ExternalMarketToken("No", "token-no", 1)
            ])),
            repository,
            new FixedTimeProvider(now.AddMinutes(3)));
        var tokenIdConflict = await tokenIdHandler.Handle(
            new RegisterMarketCommand("https://polymarket.com/event/rain-event"),
            CancellationToken.None);

        tokenIdConflict.IsFailure.Should().BeTrue();
        tokenIdConflict.Error.Single().Code.Should().Be("market.registration.identity_conflict");

        var reorderedHandler = new RegisterMarketHandler(
            new StubExternalMarketGateway(CreateExternalEvent(tokens:
            [
                new ExternalMarketToken("No", "token-no", 0),
                new ExternalMarketToken("Yes", "token-yes", 1)
            ])),
            repository,
            new FixedTimeProvider(now.AddMinutes(4)));
        var reorderedConflict = await reorderedHandler.Handle(
            new RegisterMarketCommand("https://polymarket.com/event/rain-event"),
            CancellationToken.None);

        reorderedConflict.IsFailure.Should().BeTrue();
        reorderedConflict.Error.Single().Code.Should().Be("market.registration.identity_conflict");

        var tokenSetHandler = new RegisterMarketHandler(
            new StubExternalMarketGateway(CreateExternalEvent(tokens:
            [
                new ExternalMarketToken("Yes", "token-yes", 0)
            ])),
            repository,
            new FixedTimeProvider(now.AddMinutes(5)));
        var tokenSetConflict = await tokenSetHandler.Handle(
            new RegisterMarketCommand("https://polymarket.com/event/rain-event"),
            CancellationToken.None);

        tokenSetConflict.IsFailure.Should().BeTrue();
        tokenSetConflict.Error.Single().Code.Should().Be("market.registration.identity_conflict");
    }

    [Fact]
    public async Task Registration_WithOnlyMatchingTokenIds_ShouldReturnIdentityConflict()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = CreateContext(database.ConnectionString);
        await context.Database.MigrateAsync();
        var repository = new MarketRepository(context);
        await repository.TryAddAsync(CreateMarket("external-token-id"), CancellationToken.None);
        context.ChangeTracker.Clear();
        var handler = new RegisterMarketHandler(
            new StubExternalMarketGateway(CreateExternalEvent()),
            repository,
            new FixedTimeProvider(DateTimeOffset.Parse("2026-08-27T12:00:00Z")));

        var result = await handler.Handle(
            new RegisterMarketCommand("https://polymarket.com/event/rain-event"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Code.Should().Be("market.registration.identity_conflict");
    }

    [Fact]
    public async Task MigrationRollback_WithExistingMarket_ShouldRequireEmptyMarketsTable()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = CreateContext(database.ConnectionString);
        await context.Database.MigrateAsync();
        var repository = new MarketRepository(context);
        await repository.TryAddAsync(CreateMarket(), CancellationToken.None);
        var migrator = context.GetService<IMigrator>();

        var action = () => migrator.MigrateAsync("20260721122759_InitialMarkets");

        var exception = await action.Should().ThrowAsync<PostgresException>();
        exception.Which.MessageText.Should().Contain("rollback requires an empty markets table");
    }

    private static MarketsDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<MarketsDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new MarketsDbContext(options);
    }

    private static Market CreateMarket(string? duplicateKey = null)
    {
        string Select(string key, string existing, string unique) =>
            duplicateKey is null || duplicateKey == key ? existing : unique;
        var suffix = Guid.NewGuid().ToString("N");
        var tokenSuffix = duplicateKey is null or "external-token-id" ? string.Empty : $"-{suffix}";
        var discoveredAt = DateTimeOffset.Parse("2026-08-27T09:00:00+03:00");
        var market = Market.Create(
            MarketId.Create(Guid.NewGuid()).Value,
            ExternalEventId.Create(Select("external-event-id", "event-123", $"event-{suffix}")).Value,
            EventSlug.Create(Select("event-slug", "rain-event", $"event-{suffix}")).Value,
            ExternalMarketId.Create(Select("external-market-id", "market-123", $"market-{suffix}")).Value,
            MarketSlug.Create(Select("market-slug", "will-it-rain", $"market-{suffix}")).Value,
            ConditionId.Create(Select("condition-id", "0xcondition", $"condition-{suffix}")).Value,
            "Will it rain?",
            discoveredAt,
            discoveredAt.AddDays(-1),
            discoveredAt.AddMinutes(10),
            discoveredAt.AddMinutes(20),
            discoveredAt.AddHours(1),
            discoveredAt.AddHours(1).AddMinutes(5),
            null,
            discoveredAt).Value;

        market.AddToken(TokenId.Create($"token-yes{tokenSuffix}").Value, "Yes", 0);
        market.AddToken(TokenId.Create($"token-no{tokenSuffix}").Value, "No", 1);
        return market;
    }

    private static ExternalEvent CreateExternalEvent(
        string externalEventId = "event-123",
        string firstOutcome = "Yes",
        int scheduleOffsetMinutes = 0,
        IReadOnlyList<ExternalMarketToken>? tokens = null)
    {
        var now = DateTimeOffset.Parse("2026-08-27T12:00:00Z");
        return new ExternalEvent(
            externalEventId,
            "rain-event",
            new ExternalMarket(
                ExternalMarketId: "market-123",
                Slug: "will-it-rain",
                Question: "Will it rain?",
                ConditionId: "0xcondition",
                ExternalCreatedAt: now.AddDays(-1),
                OrdersOpenedAt: null,
                GammaStartDate: now.AddMinutes(30),
                EventStartsAt: now.AddHours(1).AddMinutes(scheduleOffsetMinutes),
                EventEndsAt: now.AddHours(1).AddMinutes(5 + scheduleOffsetMinutes),
                ExternalClosedAt: null,
                UmaResolutionStatus: null,
                Active: true,
                Closed: false,
                AcceptingOrders: true,
                OrderBookEnabled: true,
                Tokens: tokens ??
                [
                    new ExternalMarketToken(firstOutcome, "token-yes", 0),
                    new ExternalMarketToken("No", "token-no", 1)
                ]));
    }

    private sealed class StubExternalMarketGateway(ExternalEvent externalEvent) : IExternalMarketGateway
    {
        public Task<Result<ExternalEvent, Error>> GetByEventSlugAsync(
            EventSlug eventSlug,
            CancellationToken cancellationToken) => Task.FromResult<Result<ExternalEvent, Error>>(externalEvent);

        public Task<Result<ExternalMarket, Error>> GetByMarketSlugAsync(
            MarketSlug slug,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
