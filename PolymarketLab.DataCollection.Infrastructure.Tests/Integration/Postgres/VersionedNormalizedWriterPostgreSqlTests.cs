using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Normalization;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Integration.Postgres;

[Collection(PostgreSqlCollection.Name)]
public sealed class VersionedNormalizedWriterPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task WriteProcessed_ShouldPersistAllTypedRowsAndCompleteLedger()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var payloads = await SeedRawMessagesAsync(database.ConnectionString, 7);
        var claims = await ClaimAsync(database.ConnectionString, 1, 7);
        var events = new[]
        {
            CreateEvent(claims[0], "last_trade_price",
                new LastTradeRecord(0.45m, 12m, TradeSide.Buy, 2m, "0xtrade")),
            CreateEvent(claims[1], "price_change",
                new PriceChangeRecord(0, "asset-a", 0.4m, 5m, TradeSide.Buy, "hash", 0.3m, 0.5m),
                new PriceChangeRecord(1, "asset-b", 0.6m, 6m, TradeSide.Sell, null, null, null)),
            CreateEvent(claims[2], "book",
                new BookSnapshotRecord("book-hash", 0.01m, 0.5m),
                new BookLevelRecord(OrderBookSide.Bid, 0, 0.4m, 10m),
                new BookLevelRecord(OrderBookSide.Ask, 0, 0.6m, 11m)),
            CreateEvent(claims[3], "tick_size_change",
                new TickSizeChangeRecord(0.01m, 0.001m)),
            CreateEvent(claims[4], "best_bid_ask",
                new BestBidAskRecord(0.4m, 0.6m, 0.2m)),
            CreateEvent(claims[5], "new_market",
                CreateNewMarketRecord(),
                new NewMarketAssetRecord(0, "asset-yes", "Yes"),
                new NewMarketAssetRecord(1, "asset-no", "No")),
            CreateEvent(claims[6], "market_resolved",
                new MarketResolvedRecord("market-1", "asset-yes", "Yes"),
                new MarketResolvedAssetRecord(0, "asset-yes"),
                new MarketResolvedAssetRecord(1, "asset-no"))
        };

        var statuses = new List<NormalizationWriteStatus>();
        for (var index = 0; index < claims.Count; index++)
        {
            statuses.Add(await WriteAsync(
                database.ConnectionString,
                claims[index],
                NormalizationCompletion.Processed([events[index]])));
        }

        statuses.Should().OnlyContain(status => status == NormalizationWriteStatus.Written);
        (await CountAsync(database.ConnectionString, "normalized_events")).Should().Be(7);
        (await CountAsync(database.ConnectionString, "last_trade_price")).Should().Be(1);
        (await CountAsync(database.ConnectionString, "price_change")).Should().Be(2);
        (await CountAsync(database.ConnectionString, "book_snapshots")).Should().Be(1);
        (await CountAsync(database.ConnectionString, "book_levels")).Should().Be(2);
        (await CountAsync(database.ConnectionString, "tick_size_changes")).Should().Be(1);
        (await CountAsync(database.ConnectionString, "best_bid_asks")).Should().Be(1);
        (await CountAsync(database.ConnectionString, "new_markets")).Should().Be(1);
        (await CountAsync(database.ConnectionString, "new_market_assets")).Should().Be(2);
        (await CountAsync(database.ConnectionString, "market_resolutions")).Should().Be(1);
        (await CountAsync(database.ConnectionString, "market_resolution_assets")).Should().Be(2);
        (await QueryIntsAsync(
            database.ConnectionString,
            "SELECT status FROM data_collection.raw_message_normalizations ORDER BY raw_message_id"))
            .Should().OnlyContain(status => status == (int)NormalizationStatus.Processed);
        (await ReadPayloadsAsync(database.ConnectionString)).Should().BeEquivalentTo(payloads);
    }

    [Fact]
    public async Task WriteProcessed_ArrayEventsShouldBeCommittedTogether()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        await SeedRawMessagesAsync(database.ConnectionString, 1);
        var claim = (await ClaimAsync(database.ConnectionString, 1, 1)).Single();
        var completion = NormalizationCompletion.Processed(
        [
            CreateEvent(claim, 0, "last_trade_price",
                new LastTradeRecord(0.4m, 1m, TradeSide.Buy, null, null)),
            CreateEvent(claim, 1, "last_trade_price",
                new LastTradeRecord(0.6m, 2m, TradeSide.Sell, null, null))
        ]);

        var status = await WriteAsync(database.ConnectionString, claim, completion);

        status.Should().Be(NormalizationWriteStatus.Written);
        (await CountAsync(database.ConnectionString, "normalized_events")).Should().Be(2);
        (await CountAsync(database.ConnectionString, "last_trade_price")).Should().Be(2);
        var ledger = await ReadLedgerAsync(database.ConnectionString);
        ledger[claim.Message.RawMessageId].Status.Should().Be((int)NormalizationStatus.Processed);
    }

    [Fact]
    public async Task WriteTerminalOutcomes_ShouldNotCreateProjectionRows()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        await SeedRawMessagesAsync(database.ConnectionString, 3);
        var claims = await ClaimAsync(database.ConnectionString, 1, 3);
        var invalidIssue = new NormalizationIssue("json.invalid", "Malformed JSON.");
        var unsupportedIssue = new NormalizationIssue("event.unsupported", "Unknown event type.");
        var failedIssue = new NormalizationIssue("processing.failed", "Technical failure.");

        var invalid = await WriteAsync(
            database.ConnectionString,
            claims[0],
            NormalizationCompletion.Invalid(invalidIssue));
        var unsupported = await WriteAsync(
            database.ConnectionString,
            claims[1],
            NormalizationCompletion.Unsupported(unsupportedIssue));
        var failed = await WriteAsync(
            database.ConnectionString,
            claims[2],
            NormalizationCompletion.Failed(failedIssue));

        invalid.Should().Be(NormalizationWriteStatus.Written);
        unsupported.Should().Be(NormalizationWriteStatus.Written);
        failed.Should().Be(NormalizationWriteStatus.Written);
        (await CountAsync(database.ConnectionString, "normalized_events")).Should().Be(0);
        var ledger = await ReadLedgerAsync(database.ConnectionString);
        ledger[claims[0].Message.RawMessageId].Should().Be(
            new Ledger((int)NormalizationStatus.Invalid, "json.invalid", "Malformed JSON."));
        ledger[claims[1].Message.RawMessageId].Should().Be(
            new Ledger((int)NormalizationStatus.Unsupported, "event.unsupported", "Unknown event type."));
        ledger[claims[2].Message.RawMessageId].Should().Be(
            new Ledger((int)NormalizationStatus.Failed, "processing.failed", "Technical failure."));
    }

    [Fact]
    public async Task WriteProcessed_ShouldBeIdempotentAndAllowProjectionVersions()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        await SeedRawMessagesAsync(database.ConnectionString, 1);
        var claimV1 = (await ClaimAsync(database.ConnectionString, 1, 1)).Single();
        var completionV1 = NormalizationCompletion.Processed(
        [
            CreateEvent(claimV1, "last_trade_price",
                new LastTradeRecord(0.4m, 1m, TradeSide.Buy, null, null))
        ]);

        var first = await WriteAsync(database.ConnectionString, claimV1, completionV1);
        var repeated = await WriteAsync(database.ConnectionString, claimV1, completionV1);
        var claimV2 = (await ClaimAsync(database.ConnectionString, 2, 1)).Single();
        var secondVersion = await WriteAsync(
            database.ConnectionString,
            claimV2,
            NormalizationCompletion.Processed(
            [
                CreateEvent(claimV2, "last_trade_price",
                    new LastTradeRecord(0.5m, 2m, TradeSide.Sell, null, null))
            ]));

        first.Should().Be(NormalizationWriteStatus.Written);
        repeated.Should().Be(NormalizationWriteStatus.AlreadyCompleted);
        secondVersion.Should().Be(NormalizationWriteStatus.Written);
        (await CountAsync(database.ConnectionString, "normalized_events")).Should().Be(2);
        (await QueryIntsAsync(
            database.ConnectionString,
            "SELECT projection_version FROM data_collection.normalized_events ORDER BY projection_version"))
            .Should().Equal(1, 2);
    }

    [Fact]
    public async Task WriteProcessed_DatabaseErrorShouldRollbackAndPreserveRawPayload()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var payloads = await SeedRawMessagesAsync(database.ConnectionString, 1);
        var claim = (await ClaimAsync(database.ConnectionString, 1, 1)).Single();
        var completion = NormalizationCompletion.Processed(
        [
            CreateEvent(claim, 0, "last_trade_price",
                new LastTradeRecord(0.4m, 1m, TradeSide.Buy, null, null)),
            CreateEvent(claim, 1, "price_change",
                new PriceChangeRecord(
                    0,
                    "asset-a",
                    0.5m,
                    decimal.MaxValue,
                    TradeSide.Buy,
                    null,
                    null,
                    null))
        ]);

        var write = async () => await WriteAsync(database.ConnectionString, claim, completion);

        await write.Should().ThrowAsync<DbUpdateException>();
        (await CountAsync(database.ConnectionString, "normalized_events")).Should().Be(0);
        (await CountAsync(database.ConnectionString, "last_trade_price")).Should().Be(0);
        (await CountAsync(database.ConnectionString, "price_change")).Should().Be(0);
        var ledger = await ReadLedgerAsync(database.ConnectionString);
        ledger[claim.Message.RawMessageId].Status.Should().Be((int)NormalizationStatus.Processing);
        (await ReadPayloadsAsync(database.ConnectionString)).Should().BeEquivalentTo(payloads);
    }

    [Fact]
    public async Task WriteProcessed_StaleAttemptShouldNotCompleteReclaimedRow()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        await SeedRawMessagesAsync(database.ConnectionString, 1);
        var staleClaim = (await ClaimAsync(database.ConnectionString, 1, 1)).Single();
        await ExecuteAsync(
            database.ConnectionString,
            """
            UPDATE data_collection.raw_message_normalizations
            SET claimed_at = CURRENT_TIMESTAMP - interval '1 hour'
            WHERE raw_message_id = @raw_id AND projection_version = 1
            """,
            new NpgsqlParameter("raw_id", staleClaim.Message.RawMessageId));
        var currentClaim = (await ClaimAsync(database.ConnectionString, 1, 1)).Single();

        var staleResult = await WriteAsync(
            database.ConnectionString,
            staleClaim,
            NormalizationCompletion.Processed(
            [
                CreateEvent(staleClaim, "last_trade_price",
                    new LastTradeRecord(0.4m, 1m, TradeSide.Buy, null, null))
            ]));
        var currentResult = await WriteAsync(
            database.ConnectionString,
            currentClaim,
            NormalizationCompletion.Processed(
            [
                CreateEvent(currentClaim, "last_trade_price",
                    new LastTradeRecord(0.5m, 1m, TradeSide.Sell, null, null))
            ]));

        staleResult.Should().Be(NormalizationWriteStatus.ClaimLost);
        currentResult.Should().Be(NormalizationWriteStatus.Written);
        (await CountAsync(database.ConnectionString, "normalized_events")).Should().Be(1);
    }

    private async Task<PostgreSqlTestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await fixture.CreateDatabaseAsync();
        await using var context = CreateContext(database.ConnectionString);
        await context.Database.MigrateAsync();
        return database;
    }

    private static async Task<IReadOnlyList<ClaimedRawMessage>> ClaimAsync(
        string connectionString,
        int projectionVersion,
        int batchSize)
    {
        await using var context = CreateContext(connectionString);
        return await new RawMessageNormalizationClaimRepository(context).ClaimBatchAsync(
            projectionVersion,
            batchSize,
            ClaimTimeout,
            default);
    }

    private static async Task<NormalizationWriteStatus> WriteAsync(
        string connectionString,
        ClaimedRawMessage claim,
        NormalizationCompletion completion)
    {
        await using var context = CreateContext(connectionString);
        return await new VersionedNormalizedWriter(context, TimeProvider.System)
            .WriteAsync(claim, completion, default);
    }

    private static NormalizedEvent CreateEvent(
        ClaimedRawMessage claim,
        string eventType,
        params NormalizedRecord[] records) =>
        CreateEvent(claim, 0, eventType, records);

    private static NormalizedEvent CreateEvent(
        ClaimedRawMessage claim,
        int rawItemIndex,
        string eventType,
        params NormalizedRecord[] records) =>
        new(
            claim.Message.RawMessageId,
            rawItemIndex,
            claim.ProjectionVersion,
            normalizerVersion: 1,
            eventType,
            claim.Message.SessionId,
            claim.Message.ReceivedAt,
            sourceTimestamp: 1_765_728_000_000,
            marketConditionId: "condition-1",
            assetId: "asset-1",
            records);

    private static NewMarketRecord CreateNewMarketRecord() =>
        new(
            "market-1",
            "Question?",
            "market-slug",
            "Description",
            true,
            string.Empty,
            null,
            string.Empty,
            0.01m,
            string.Empty,
            0m,
            false,
            new NewMarketEventMessage(
                "event-1",
                "ticker",
                "event-slug",
                "Event title",
                "Event description"),
            new NewMarketFeeSchedule(1m, 2m, 3m, false));

    private static DataCollectionDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<DataCollectionDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new DataCollectionDbContext(options);
    }

    private static async Task<IReadOnlyList<byte[]>> SeedRawMessagesAsync(
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

        var payloads = new List<byte[]>(count);
        for (var index = 0; index < count; index++)
        {
            var payload = new byte[] { (byte)index, 1, 2, 255 };
            payloads.Add(payload);
            await ExecuteAsync(
                connectionString,
                """
                INSERT INTO data_collection.raw_market_messages
                    (session_id, received_at, payload)
                VALUES (@session_id, @received_at, @payload)
                """,
                new NpgsqlParameter("session_id", sessionId),
                new NpgsqlParameter("received_at", receivedAt.AddSeconds(index)),
                new NpgsqlParameter("payload", payload));
        }

        return payloads;
    }

    private static async Task<Dictionary<long, Ledger>> ReadLedgerAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT raw_message_id, status, error_code, error_message
            FROM data_collection.raw_message_normalizations
            ORDER BY raw_message_id
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new Dictionary<long, Ledger>();
        while (await reader.ReadAsync())
        {
            result.Add(
                reader.GetInt64(0),
                new Ledger(
                    reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<byte[]>> ReadPayloadsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT payload FROM data_collection.raw_market_messages ORDER BY id",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var payloads = new List<byte[]>();
        while (await reader.ReadAsync())
            payloads.Add(reader.GetFieldValue<byte[]>(0));
        return payloads;
    }

    private static Task<int> CountAsync(string connectionString, string table) =>
        ExecuteScalarAsync<int>(
            connectionString,
            $"SELECT count(*)::integer FROM data_collection.{table}");

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
        string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private sealed record Ledger(int Status, string? ErrorCode, string? ErrorMessage);
}
