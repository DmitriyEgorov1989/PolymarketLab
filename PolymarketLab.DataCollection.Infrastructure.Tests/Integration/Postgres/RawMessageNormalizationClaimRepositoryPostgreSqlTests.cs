using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Normalization;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Integration.Postgres;

[Collection(PostgreSqlCollection.Name)]
public sealed class RawMessageNormalizationClaimRepositoryPostgreSqlTests(
    PostgreSqlFixture fixture)
{
    private static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task ClaimBatch_ShouldRespectLimitAndOrderByRawMessageId()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var rawMessageIds = await SeedRawMessagesAsync(database.ConnectionString, 7);
        await using var context = CreateContext(database.ConnectionString);
        var repository = new RawMessageNormalizationClaimRepository(context);

        var firstBatch = await repository.ClaimBatchAsync(1, 2, ClaimTimeout, default);
        var secondBatch = await repository.ClaimBatchAsync(1, 10, ClaimTimeout, default);

        firstBatch.Select(claim => claim.Message.RawMessageId)
            .Should().Equal(rawMessageIds.Take(2));
        secondBatch.Select(claim => claim.Message.RawMessageId)
            .Should().Equal(rawMessageIds.Skip(2));
        firstBatch.Should().HaveCount(2);
        secondBatch.Should().HaveCount(5);
        firstBatch.Concat(secondBatch).Should().OnlyContain(claim =>
            claim.ProjectionVersion == 1 && claim.AttemptCount == 1);
    }

    [Fact]
    public async Task ClaimBatch_ShouldSkipTerminalRowsButNewVersionShouldSeeEntireArchive()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var rawMessageIds = await SeedRawMessagesAsync(database.ConnectionString, 5);
        var terminalStatuses = new[]
        {
            NormalizationStatus.Processed,
            NormalizationStatus.Unsupported,
            NormalizationStatus.Invalid,
            NormalizationStatus.Failed
        };
        for (var index = 0; index < terminalStatuses.Length; index++)
        {
            await InsertLedgerAsync(
                database.ConnectionString,
                rawMessageIds[index],
                projectionVersion: 1,
                terminalStatuses[index],
                attemptCount: 1,
                claimedAt: DateTimeOffset.UtcNow.AddHours(-1));
        }

        await using var contextV1 = CreateContext(database.ConnectionString);
        await using var contextV2 = CreateContext(database.ConnectionString);
        var versionOne = await new RawMessageNormalizationClaimRepository(contextV1)
            .ClaimBatchAsync(1, 10, ClaimTimeout, default);
        var versionTwo = await new RawMessageNormalizationClaimRepository(contextV2)
            .ClaimBatchAsync(2, 10, ClaimTimeout, default);

        versionOne.Select(claim => claim.Message.RawMessageId)
            .Should().Equal(rawMessageIds[^1]);
        versionTwo.Select(claim => claim.Message.RawMessageId)
            .Should().Equal(rawMessageIds);
    }

    [Fact]
    public async Task ClaimBatch_ShouldRecoverOnlyStaleProcessingRows()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var rawMessageIds = await SeedRawMessagesAsync(database.ConnectionString, 3);
        var now = DateTimeOffset.UtcNow;
        await InsertLedgerAsync(
            database.ConnectionString,
            rawMessageIds[0],
            1,
            NormalizationStatus.Processing,
            attemptCount: 1,
            claimedAt: now);
        await InsertLedgerAsync(
            database.ConnectionString,
            rawMessageIds[1],
            1,
            NormalizationStatus.Processing,
            attemptCount: 2,
            claimedAt: now.Subtract(ClaimTimeout).AddSeconds(-1));
        await InsertLedgerAsync(
            database.ConnectionString,
            rawMessageIds[2],
            1,
            NormalizationStatus.Processing,
            attemptCount: 4,
            claimedAt: null);
        await using var context = CreateContext(database.ConnectionString);

        var claimed = await new RawMessageNormalizationClaimRepository(context)
            .ClaimBatchAsync(1, 10, ClaimTimeout, default);

        claimed.Select(claim => claim.Message.RawMessageId)
            .Should().Equal(rawMessageIds.Skip(1));
        claimed.Select(claim => claim.AttemptCount).Should().Equal(3, 5);
        var leases = await ReadLeasesAsync(database.ConnectionString, 1);
        leases[rawMessageIds[0]].AttemptCount.Should().Be(1);
        leases[rawMessageIds[0]].ClaimedAt.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
        leases[rawMessageIds[1]].ClaimedAt.Should().BeAfter(now);
        leases[rawMessageIds[2]].ClaimedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ClaimBatch_ConcurrentRepositoriesShouldReturnDisjointRows()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var rawMessageIds = await SeedRawMessagesAsync(database.ConnectionString, 20);
        await using var firstContext = CreateContext(database.ConnectionString);
        await using var secondContext = CreateContext(database.ConnectionString);
        var firstRepository = new RawMessageNormalizationClaimRepository(firstContext);
        var secondRepository = new RawMessageNormalizationClaimRepository(secondContext);

        var firstTask = firstRepository.ClaimBatchAsync(1, 10, ClaimTimeout, default);
        var secondTask = secondRepository.ClaimBatchAsync(1, 10, ClaimTimeout, default);
        var batches = await Task.WhenAll(firstTask, secondTask);
        var firstIds = batches[0].Select(claim => claim.Message.RawMessageId).ToArray();
        var secondIds = batches[1].Select(claim => claim.Message.RawMessageId).ToArray();

        firstIds.Should().HaveCount(10);
        secondIds.Should().HaveCount(10);
        firstIds.Should().NotIntersectWith(secondIds);
        firstIds.Concat(secondIds).Order().Should().Equal(rawMessageIds);
    }

    private async Task<PostgreSqlTestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await fixture.CreateDatabaseAsync();
        await using var context = CreateContext(database.ConnectionString);
        await context.Database.MigrateAsync();
        return database;
    }

    private static DataCollectionDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<DataCollectionDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new DataCollectionDbContext(options);
    }

    private static async Task<IReadOnlyList<long>> SeedRawMessagesAsync(
        string connectionString,
        int count)
    {
        var sessionId = Guid.NewGuid();
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

        var rawMessageIds = new List<long>(count);
        for (var index = 0; index < count; index++)
        {
            rawMessageIds.Add(await ExecuteScalarAsync<long>(
                connectionString,
                """
                INSERT INTO data_collection.raw_market_messages
                    (session_id, received_at, payload)
                VALUES (@session_id, @received_at, @payload)
                RETURNING id
                """,
                new NpgsqlParameter("session_id", sessionId),
                new NpgsqlParameter("received_at", receivedAt.AddSeconds(index)),
                new NpgsqlParameter("payload", new byte[] { (byte)index })));
        }

        return rawMessageIds;
    }

    private static Task InsertLedgerAsync(
        string connectionString,
        long rawMessageId,
        int projectionVersion,
        NormalizationStatus status,
        int attemptCount,
        DateTimeOffset? claimedAt) =>
        ExecuteAsync(
            connectionString,
            """
            INSERT INTO data_collection.raw_message_normalizations
                (raw_message_id, projection_version, status, attempt_count, claimed_at)
            VALUES (@raw_id, @projection_version, @status, @attempt_count, @claimed_at)
            """,
            new NpgsqlParameter("raw_id", rawMessageId),
            new NpgsqlParameter("projection_version", projectionVersion),
            new NpgsqlParameter("status", (int)status),
            new NpgsqlParameter("attempt_count", attemptCount),
            new NpgsqlParameter("claimed_at", (object?)claimedAt ?? DBNull.Value));

    private static async Task<Dictionary<long, Lease>> ReadLeasesAsync(
        string connectionString,
        int projectionVersion)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT raw_message_id, attempt_count, claimed_at
            FROM data_collection.raw_message_normalizations
            WHERE projection_version = @projection_version
            ORDER BY raw_message_id
            """,
            connection);
        command.Parameters.AddWithValue("projection_version", projectionVersion);
        await using var reader = await command.ExecuteReaderAsync();
        var leases = new Dictionary<long, Lease>();
        while (await reader.ReadAsync())
        {
            leases.Add(
                reader.GetInt64(0),
                new Lease(
                    reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2)));
        }

        return leases;
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

    private sealed record Lease(int AttemptCount, DateTimeOffset? ClaimedAt);
}
