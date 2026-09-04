using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.CollectorSession;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Integration.Postgres;

[Collection(PostgreSqlCollection.Name)]
public sealed class CollectorTokenReadinessPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 28, 11, 57, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FirstEnqueuedAt =
        new(2026, 8, 28, 11, 59, 44, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondEnqueuedAt =
        new(2026, 8, 28, 11, 59, 45, TimeSpan.Zero);

    [Fact]
    public async Task RecordInitialBookEnqueuedAsync_ShouldPersistOneObservation()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var session = await InsertSessionAsync(database.ConnectionString);
        await using var context = CreateContext(database.ConnectionString);
        var repository = new CollectorTokenReadinessRepository(context);
        var tokenId = TokenId.Create("1001").Value;

        await repository.RecordInitialBookEnqueuedAsync(
            new CollectorTokenReadiness(session.Id, 1, tokenId, FirstEnqueuedAt),
            CancellationToken.None);

        var readiness = await repository.GetAsync(session.Id, 1, CancellationToken.None);
        readiness.Should().ContainSingle().Which.Should()
            .Be(new CollectorTokenReadiness(session.Id, 1, tokenId, FirstEnqueuedAt));
    }

    [Fact]
    public async Task RecordInitialBookEnqueuedAsync_DuplicateInSameEpoch_ShouldPreserveFirstTimestamp()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var session = await InsertSessionAsync(database.ConnectionString);
        await using var context = CreateContext(database.ConnectionString);
        var repository = new CollectorTokenReadinessRepository(context);
        var tokenId = TokenId.Create("1001").Value;

        await repository.RecordInitialBookEnqueuedAsync(
            new CollectorTokenReadiness(session.Id, 1, tokenId, FirstEnqueuedAt),
            CancellationToken.None);
        await repository.RecordInitialBookEnqueuedAsync(
            new CollectorTokenReadiness(session.Id, 1, tokenId, SecondEnqueuedAt),
            CancellationToken.None);

        var readiness = await repository.GetAsync(session.Id, 1, CancellationToken.None);
        readiness.Should().ContainSingle().Which.InitialBookEnqueuedAt
            .Should().Be(FirstEnqueuedAt);
    }

    [Fact]
    public async Task GetAsync_WithDifferentEpoch_ShouldNotReturnObservation()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var session = await InsertSessionAsync(database.ConnectionString);
        await using var context = CreateContext(database.ConnectionString);
        var repository = new CollectorTokenReadinessRepository(context);

        await repository.RecordInitialBookEnqueuedAsync(
            new CollectorTokenReadiness(
                session.Id,
                1,
                TokenId.Create("1001").Value,
                FirstEnqueuedAt),
            CancellationToken.None);

        var readiness = await repository.GetAsync(session.Id, 2, CancellationToken.None);
        readiness.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordInitialBookEnqueuedAsync_SecondToken_ShouldBecomeReadyIndependently()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var session = await InsertSessionAsync(database.ConnectionString);
        await using var context = CreateContext(database.ConnectionString);
        var repository = new CollectorTokenReadinessRepository(context);
        var firstTokenId = TokenId.Create("1001").Value;
        var secondTokenId = TokenId.Create("1002").Value;

        await repository.RecordInitialBookEnqueuedAsync(
            new CollectorTokenReadiness(session.Id, 1, firstTokenId, FirstEnqueuedAt),
            CancellationToken.None);
        await repository.RecordInitialBookEnqueuedAsync(
            new CollectorTokenReadiness(session.Id, 1, secondTokenId, SecondEnqueuedAt),
            CancellationToken.None);

        var readiness = await repository.GetAsync(session.Id, 1, CancellationToken.None);
        readiness.Select(observation => observation.TokenId.Value)
            .Should().BeEquivalentTo("1001", "1002");
        readiness.Select(observation => observation.InitialBookEnqueuedAt)
            .Should().BeEquivalentTo(
                new[] { FirstEnqueuedAt, SecondEnqueuedAt });
    }

    [Fact]
    public async Task RecordInitialBookEnqueuedAsync_WithNonPositiveEpoch_ShouldViolateCheckConstraint()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var session = await InsertSessionAsync(database.ConnectionString);
        await using var context = CreateContext(database.ConnectionString);
        var repository = new CollectorTokenReadinessRepository(context);

        Func<Task> record = () => repository.RecordInitialBookEnqueuedAsync(
            new CollectorTokenReadiness(
                session.Id,
                0,
                TokenId.Create("1001").Value,
                FirstEnqueuedAt),
            CancellationToken.None);

        await record.Should().ThrowAsync<Npgsql.PostgresException>()
            .Where(exception =>
                exception.SqlState == "23514"
                && exception.ConstraintName == "ck_collector_token_readiness_epoch_positive");
    }

    private async Task<PostgreSqlTestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await fixture.CreateDatabaseAsync();
        await using var context = CreateContext(database.ConnectionString);
        await context.Database.MigrateAsync();
        return database;
    }

    private static async Task<CollectorSession> InsertSessionAsync(string connectionString)
    {
        await using var context = CreateContext(connectionString);
        var session = CreateSession();
        await new CollectorSessionRepository(context)
            .TryAddAsync(session, CancellationToken.None);
        return session;
    }

    private static DataCollectionDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<DataCollectionDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new DataCollectionDbContext(options);
    }

    private static CollectorSession CreateSession() =>
        CollectorSession.Create(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            MarketId.Create(Guid.NewGuid()).Value,
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
