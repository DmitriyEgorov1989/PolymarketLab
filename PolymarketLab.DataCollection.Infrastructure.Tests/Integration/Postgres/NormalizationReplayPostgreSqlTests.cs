using System.Reflection;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Application.DependencyInjection;
using PolymarketLab.DataCollection.Core.Application.Normalization;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Normalization;
using PolymarketLab.DataCollection.Infrastructure.DependencyInjection;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Integration.Postgres;

[Collection(PostgreSqlCollection.Name)]
public sealed class NormalizationReplayPostgreSqlTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Replay_FiltersShouldPreserveRawAndVersionsAndBeIdempotent()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var firstSessionId = Guid.NewGuid();
        var secondSessionId = Guid.NewGuid();
        var originalPayloads = new[]
        {
            ReadFixture("book-array.json"),
            Encoding.UTF8.GetBytes("{"),
            ReadFixture("best-bid-ask.json"),
            ReadFixture("last-trade-price.json")
        };
        await SeedSessionAsync(
            database.ConnectionString,
            firstSessionId,
            originalPayloads[..2]);
        await SeedSessionAsync(
            database.ConnectionString,
            secondSessionId,
            originalPayloads[2..]);
        await using var provider = CreateProvider(database.ConnectionString);
        await ProcessSourceAsync(provider);
        var replay = provider.GetRequiredService<INormalizationReplayService>();

        var filtered = await replay.ReplayAsync(
            new NormalizationReplayFilter(
                1,
                2,
                CollectorSessionId.Create(firstSessionId).Value,
                "book"),
            default);
        var repeated = await replay.ReplayAsync(
            new NormalizationReplayFilter(
                1,
                2,
                CollectorSessionId.Create(firstSessionId).Value,
                "book"),
            default);
        var fullReplay = await replay.ReplayAsync(
            new NormalizationReplayFilter(1, 3, null, null),
            default);
        var eventReplay = await replay.ReplayAsync(
            new NormalizationReplayFilter(1, 4, null, "best_bid_ask"),
            default);
        var caseMismatch = await replay.ReplayAsync(
            new NormalizationReplayFilter(1, 5, null, "BEST_BID_ASK"),
            default);

        filtered.IsSuccess.Should().BeTrue();
        filtered.Value.Total.Should().Be(1);
        filtered.Value.Processed.Should().Be(1);
        repeated.Value.Total.Should().Be(0);
        fullReplay.Value.Total.Should().Be(4);
        fullReplay.Value.Processed.Should().Be(3);
        fullReplay.Value.Invalid.Should().Be(1);
        eventReplay.Value.Total.Should().Be(1);
        caseMismatch.Value.Total.Should().Be(0);
        (await QueryIntsAsync(
            database.ConnectionString,
            """
            SELECT raw_item_index
            FROM data_collection.normalized_events
            WHERE projection_version = 2
            ORDER BY raw_item_index
            """))
            .Should().Equal(0, 1);
        (await CountAsync(
            database.ConnectionString,
            "normalized_events",
            "projection_version = 1")).Should().Be(4);
        (await CountAsync(
            database.ConnectionString,
            "normalized_events",
            "projection_version = 3")).Should().Be(4);
        (await CountAsync(
            database.ConnectionString,
            "best_bid_asks",
            "event_id IN (SELECT id FROM data_collection.normalized_events WHERE projection_version = 4)"))
            .Should().Be(1);
        (await ReadPayloadsAsync(database.ConnectionString)).Should().BeEquivalentTo(
            originalPayloads,
            options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task ReplaySnapshot_ShouldExcludeNewLiveMessages()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var sessionId = Guid.NewGuid();
        await SeedSessionAsync(
            database.ConnectionString,
            sessionId,
            [ReadFixture("last-trade-price.json")]);
        await using var provider = CreateProvider(database.ConnectionString);
        await ProcessSourceAsync(provider);
        await using var snapshotScope = provider.CreateAsyncScope();
        var repository = snapshotScope.ServiceProvider
            .GetRequiredService<IRawMessageNormalizationReplayClaimRepository>();
        var snapshot = await repository.CaptureSnapshotAsync(default);
        var newRawId = await SeedRawMessageAsync(
            database.ConnectionString,
            sessionId,
            ReadFixture("last-trade-price.json"));
        await ProcessSourceAsync(provider);

        var claims = await repository.ClaimBatchAsync(
            new NormalizationReplayFilter(1, 2, null, null),
            snapshot,
            100,
            TimeSpan.FromMinutes(5),
            default);

        claims.Should().ContainSingle();
        claims.Should().NotContain(claim => claim.Message.RawMessageId == newRawId);
    }

    private async Task<PostgreSqlTestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await fixture.CreateDatabaseAsync();
        await using var context = CreateContext(database.ConnectionString);
        await context.Database.MigrateAsync();
        return database;
    }

    private static ServiceProvider CreateProvider(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{DataBaseOptions.SectionName}:ConnectionString"] = connectionString,
                [$"{NormalizerOptions.SectionName}:ProjectionVersion"] = "1",
                [$"{NormalizerOptions.SectionName}:BatchSize"] = "500"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddDataCollectionApplication();
        services.AddDataCollectionInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    private static async Task ProcessSourceAsync(ServiceProvider provider)
    {
        while (true)
        {
            await using var scope = provider.CreateAsyncScope();
            var result = await scope.ServiceProvider
                .GetRequiredService<INormalizationProcessor>()
                .ProcessBatchAsync(default);
            if (result.Total == 0)
                return;
        }
    }

    private static DataCollectionDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<DataCollectionDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new DataCollectionDbContext(options);
    }

    private static async Task SeedSessionAsync(
        string connectionString,
        Guid sessionId,
        IReadOnlyCollection<byte[]> payloads)
    {
        var receivedAt = DateTimeOffset.Parse("2026-08-14T10:00:00Z");
        await ExecuteAsync(
            connectionString,
            """
            INSERT INTO data_collection.collector_sessions
                (id, market_id, status, created_at)
            VALUES (@session_id, @market_id, 4, @created_at)
            """,
            new NpgsqlParameter("session_id", sessionId),
            new NpgsqlParameter("market_id", Guid.NewGuid()),
            new NpgsqlParameter("created_at", receivedAt.AddMinutes(-1)));

        foreach (var payload in payloads)
            await SeedRawMessageAsync(connectionString, sessionId, payload);
    }

    private static Task<long> SeedRawMessageAsync(
        string connectionString,
        Guid sessionId,
        byte[] payload) =>
        ExecuteScalarAsync<long>(
            connectionString,
            """
            INSERT INTO data_collection.raw_market_messages
                (session_id, received_at, payload)
            VALUES (@session_id, CURRENT_TIMESTAMP, @payload)
            RETURNING id
            """,
            new NpgsqlParameter("session_id", sessionId),
            new NpgsqlParameter("payload", payload));

    private static byte[] ReadFixture(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames().Single(name =>
            name.EndsWith($".Fixtures.Polymarket.{fileName}", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static Task<int> CountAsync(
        string connectionString,
        string table,
        string predicate) =>
        ExecuteScalarAsync<int>(
            connectionString,
            $"SELECT count(*)::integer FROM data_collection.{table} WHERE {predicate}");

    private static async Task<IReadOnlyList<int>> QueryIntsAsync(
        string connectionString,
        string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<int>();
        while (await reader.ReadAsync())
            values.Add(reader.GetInt32(0));
        return values;
    }

    private static async Task<IReadOnlyList<byte[]>> ReadPayloadsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT payload FROM data_collection.raw_market_messages ORDER BY id",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<byte[]>();
        while (await reader.ReadAsync())
            values.Add(reader.GetFieldValue<byte[]>(0));
        return values;
    }

    private static async Task ExecuteAsync(
        string connectionString,
        string sql,
        params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ExecuteScalarAsync<T>(
        string connectionString,
        string sql,
        params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
