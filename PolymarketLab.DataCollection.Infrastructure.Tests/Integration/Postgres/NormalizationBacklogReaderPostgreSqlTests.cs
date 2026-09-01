using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Normalization;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Integration.Postgres;

[Collection(PostgreSqlCollection.Name)]
public sealed class NormalizationBacklogReaderPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task Read_ShouldDistinguishClaimablePendingFromVersionedLag()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var rawIds = await SeedRawMessagesAsync(database.ConnectionString, 7);
        await InsertLedgerAsync(database.ConnectionString, rawIds[1], 1, NormalizationStatus.Pending);
        await InsertLedgerAsync(
            database.ConnectionString,
            rawIds[2],
            1,
            NormalizationStatus.Processing,
            DateTimeOffset.UtcNow);
        await InsertLedgerAsync(
            database.ConnectionString,
            rawIds[3],
            1,
            NormalizationStatus.Processing,
            DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(10)));
        await InsertLedgerAsync(database.ConnectionString, rawIds[4], 1, NormalizationStatus.Processed);
        await InsertLedgerAsync(database.ConnectionString, rawIds[5], 1, NormalizationStatus.Unsupported);
        await InsertLedgerAsync(database.ConnectionString, rawIds[6], 1, NormalizationStatus.Failed);
        await using var context = CreateContext(database.ConnectionString);
        var reader = new NormalizationBacklogReader(context);

        var versionOne = await reader.ReadAsync(1, ClaimTimeout, default);
        var versionTwo = await reader.ReadAsync(2, ClaimTimeout, default);

        versionOne.Should().Be(new NormalizationBacklogSnapshot(1, 3, 4));
        versionTwo.Should().Be(new NormalizationBacklogSnapshot(2, 7, 7));
    }

    [Fact]
    public async Task Read_CancelledOperation_ShouldPropagateCancellation()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        await using var context = CreateContext(database.ConnectionString);
        var reader = new NormalizationBacklogReader(context);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = async () => await reader.ReadAsync(1, ClaimTimeout, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
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

        var ids = new List<long>(count);
        for (var index = 0; index < count; index++)
        {
            ids.Add(await ExecuteScalarAsync<long>(
                connectionString,
                """
                INSERT INTO data_collection.raw_market_messages
                    (session_id, connection_epoch, received_at, payload)
                VALUES (@session_id, 1, @received_at, @payload)
                RETURNING id
                """,
                new NpgsqlParameter("session_id", sessionId),
                new NpgsqlParameter("received_at", receivedAt.AddSeconds(index)),
                new NpgsqlParameter("payload", new byte[] { 1, 2, 3 })));
        }

        return ids;
    }

    private static Task InsertLedgerAsync(
        string connectionString,
        long rawMessageId,
        int projectionVersion,
        NormalizationStatus status,
        DateTimeOffset? claimedAt = null) =>
        ExecuteAsync(
            connectionString,
            """
            INSERT INTO data_collection.raw_message_normalizations
                (raw_message_id, projection_version, status, attempt_count, claimed_at)
            VALUES (@raw_message_id, @projection_version, @status, @attempt_count, @claimed_at)
            """,
            new NpgsqlParameter("raw_message_id", rawMessageId),
            new NpgsqlParameter("projection_version", projectionVersion),
            new NpgsqlParameter("status", (int)status),
            new NpgsqlParameter(
                "attempt_count",
                status == NormalizationStatus.Processing ? 1 : 0),
            new NpgsqlParameter("claimed_at", (object?)claimedAt ?? DBNull.Value));

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
