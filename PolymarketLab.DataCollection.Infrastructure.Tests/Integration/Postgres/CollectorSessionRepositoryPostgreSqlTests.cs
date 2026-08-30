using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.CollectorSession;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Integration.Postgres;

[Collection(PostgreSqlCollection.Name)]
public sealed class CollectorSessionRepositoryPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 27, 11, 57, 0, TimeSpan.Zero);

    [Fact]
    public async Task ConcurrentInserts_ForDifferentMarkets_ShouldAllowOneGlobalWinner()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var first = CreateSession(MarketId.Create(Guid.NewGuid()).Value);
        var second = CreateSession(MarketId.Create(Guid.NewGuid()).Value);

        var results = await Task.WhenAll(
            InsertAsync(database.ConnectionString, first),
            InsertAsync(database.ConnectionString, second));

        results.Should().ContainSingle(result =>
            result == CollectorSessionInsertStatus.Inserted);
        results.Should().ContainSingle(result =>
            result == CollectorSessionInsertStatus.ExclusiveSessionConflict);
        await using var context = CreateContext(database.ConnectionString);
        var exclusive = await new CollectorSessionRepository(context)
            .GetExclusiveAsync(CancellationToken.None);
        exclusive.Should().NotBeNull();
        exclusive!.Id.Should().BeOneOf(first.Id, second.Id);
    }

    [Fact]
    public async Task ConcurrentInserts_ForSameMarket_ShouldAllowOneIdempotentWinner()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var marketId = MarketId.Create(Guid.NewGuid()).Value;
        var first = CreateSession(marketId);
        var second = CreateSession(marketId);

        var results = await Task.WhenAll(
            InsertAsync(database.ConnectionString, first),
            InsertAsync(database.ConnectionString, second));

        results.Should().ContainSingle(result =>
            result == CollectorSessionInsertStatus.Inserted);
        results.Should().ContainSingle(result =>
            result == CollectorSessionInsertStatus.ExclusiveSessionConflict);
        await using var context = CreateContext(database.ConnectionString);
        var exclusive = await new CollectorSessionRepository(context)
            .GetExclusiveAsync(CancellationToken.None);
        exclusive!.MarketId.Should().Be(marketId);
    }

    [Fact]
    public async Task TerminalSession_ShouldReleaseGlobalSlot()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var first = CreateSession(MarketId.Create(Guid.NewGuid()).Value);
        await using (var firstContext = CreateContext(database.ConnectionString))
        {
            var repository = new CollectorSessionRepository(firstContext);
            (await repository.TryAddAsync(first, CancellationToken.None)).Value
                .Should().Be(CollectorSessionInsertStatus.Inserted);
            first.Stop(CreatedAt.AddMinutes(1), CollectorStopReason.MarketClosed);
            (await repository.TryUpdateAsync(
                first,
                CollectorSessionStatus.Scheduled,
                CancellationToken.None)).Value.Should().Be(CollectorSessionUpdateStatus.Updated);
        }

        var second = CreateSession(MarketId.Create(Guid.NewGuid()).Value);
        var result = await InsertAsync(database.ConnectionString, second);

        result.Should().Be(CollectorSessionInsertStatus.Inserted);
    }

    [Fact]
    public async Task TryUpdateAsync_AfterDetachedRead_ShouldPreserveExclusiveSlot()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var session = CreateSession(MarketId.Create(Guid.NewGuid()).Value);
        await InsertAsync(database.ConnectionString, session);
        await using var context = CreateContext(database.ConnectionString);
        var repository = new CollectorSessionRepository(context);
        var persisted = await repository.GetByIdAsync(session.Id, CancellationToken.None);
        persisted!.BeginPreparation(CreatedAt).IsSuccess.Should().BeTrue();

        var result = await repository.TryUpdateAsync(
            persisted,
            CollectorSessionStatus.Scheduled,
            CancellationToken.None);

        result.Value.Should().Be(CollectorSessionUpdateStatus.Updated);
        await using var verificationContext = CreateContext(database.ConnectionString);
        var updated = await new CollectorSessionRepository(verificationContext)
            .GetByIdAsync(session.Id, CancellationToken.None);
        updated!.Status.Should().Be(CollectorSessionStatus.Starting);
    }

    [Fact]
    public async Task TryUpdateAsync_AfterConcurrencyConflict_ShouldAllowRetryInSameContext()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var session = CreateSession(MarketId.Create(Guid.NewGuid()).Value);
        await InsertAsync(database.ConnectionString, session);
        await using var staleContext = CreateContext(database.ConnectionString);
        var staleRepository = new CollectorSessionRepository(staleContext);
        var stale = await staleRepository.GetByIdAsync(session.Id, CancellationToken.None);

        await using (var winningContext = CreateContext(database.ConnectionString))
        {
            var winningRepository = new CollectorSessionRepository(winningContext);
            var winning = await winningRepository.GetByIdAsync(session.Id, CancellationToken.None);
            winning!.BeginPreparation(CreatedAt).IsSuccess.Should().BeTrue();
            (await winningRepository.TryUpdateAsync(
                winning,
                CollectorSessionStatus.Scheduled,
                CancellationToken.None)).Value.Should().Be(CollectorSessionUpdateStatus.Updated);
        }

        stale!.Interrupt(CreatedAt, CollectorStopReason.ProcessTerminated)
            .IsSuccess.Should().BeTrue();
        (await staleRepository.TryUpdateAsync(
            stale,
            CollectorSessionStatus.Scheduled,
            CancellationToken.None)).Value.Should().Be(
                CollectorSessionUpdateStatus.ConcurrencyConflict);
        var refreshed = await staleRepository.GetByIdAsync(
            session.Id,
            CancellationToken.None);
        refreshed!.MarkStopping().IsSuccess.Should().BeTrue();

        var retry = await staleRepository.TryUpdateAsync(
            refreshed,
            CollectorSessionStatus.Starting,
            CancellationToken.None);

        retry.Value.Should().Be(CollectorSessionUpdateStatus.Updated);
    }

    [Fact]
    public async Task Read_ShouldRestoreExactSnapshotAndTokenOrder()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var session = CreateSession(MarketId.Create(Guid.NewGuid()).Value);
        await InsertAsync(database.ConnectionString, session);
        await using var context = CreateContext(database.ConnectionString);

        var persisted = await new CollectorSessionRepository(context)
            .GetByIdAsync(session.Id, CancellationToken.None);

        persisted.Should().NotBeNull();
        persisted!.ConditionId.Should().Be("0xabc");
        persisted.ProjectionVersion.Should().Be(3);
        persisted.Tokens.Select(token => token.TokenId.Value)
            .Should().Equal("1001", "1002");
    }

    private async Task<PostgreSqlTestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await fixture.CreateDatabaseAsync();
        await using var context = CreateContext(database.ConnectionString);
        await context.Database.MigrateAsync();
        return database;
    }

    private static async Task<CollectorSessionInsertStatus> InsertAsync(
        string connectionString,
        CollectorSessionAggregate session)
    {
        await using var context = CreateContext(connectionString);
        var result = await new CollectorSessionRepository(context)
            .TryAddAsync(session, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static DataCollectionDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<DataCollectionDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new DataCollectionDbContext(options);
    }

    private static CollectorSessionAggregate CreateSession(MarketId marketId) =>
        CollectorSessionAggregate.Create(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            marketId,
            "event-123",
            "btc-updown-5m-1200",
            "market-123",
            "btc-updown-5m-1200",
            "0xabc",
            CreatedAt.AddMinutes(3),
            CreatedAt.AddMinutes(8),
            3,
            [
                new CollectorSessionTokenDefinition(TokenId.Create("1001").Value, "Yes", 0),
                new CollectorSessionTokenDefinition(TokenId.Create("1002").Value, "No", 1)
            ],
            CreatedAt).Value;
}
