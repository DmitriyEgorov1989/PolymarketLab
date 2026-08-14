using System.Reflection;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PolymarketLab.DataCollection.Core.Application.Normalization;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Normalization;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Integration.Postgres;

[Collection(PostgreSqlCollection.Name)]
public sealed class NormalizationProcessorPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task ProcessBatch_AllEventTypes_ShouldPersistHeadersTypedRowsAndProcessedLedgers()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixtures = new[]
        {
            "last-trade-price.json",
            "price-change.json",
            "book.json",
            "tick-size-change.json",
            "best-bid-ask.json",
            "new-market.json",
            "market-resolved.json"
        };
        var payloads = fixtures.Select(ReadFixture).ToArray();
        await SeedRawMessagesAsync(database.ConnectionString, payloads);

        var result = await ProcessAsync(database.ConnectionString, 1, fixtures.Length);

        result.Should().BeEquivalentTo(new NormalizationBatchResult(
            7, 7, 0, 0, 0, result.FirstRawMessageId, result.LastRawMessageId));
        result.FirstRawMessageId.Should().BePositive();
        result.LastRawMessageId.Should().Be(result.FirstRawMessageId + 6);
        (await QueryIntsAsync(
            database.ConnectionString,
            "SELECT status FROM data_collection.raw_message_normalizations ORDER BY raw_message_id"))
            .Should().OnlyContain(status => status == (int)NormalizationStatus.Processed);
        (await QueryStringsAsync(
            database.ConnectionString,
            "SELECT event_type FROM data_collection.normalized_events ORDER BY raw_message_id"))
            .Should().Equal(
                "last_trade_price",
                "price_change",
                "book",
                "tick_size_change",
                "best_bid_ask",
                "new_market",
                "market_resolved");
        (await CountAsync(database.ConnectionString, "normalized_events")).Should().Be(7);
        (await CountAsync(database.ConnectionString, "last_trade_price")).Should().Be(1);
        (await CountAsync(database.ConnectionString, "price_change")).Should().Be(2);
        (await CountAsync(database.ConnectionString, "book_snapshots")).Should().Be(1);
        (await CountAsync(database.ConnectionString, "book_levels")).Should().Be(43);
        (await CountAsync(database.ConnectionString, "tick_size_changes")).Should().Be(1);
        (await CountAsync(database.ConnectionString, "best_bid_asks")).Should().Be(1);
        (await CountAsync(database.ConnectionString, "new_markets")).Should().Be(1);
        (await CountAsync(database.ConnectionString, "new_market_assets")).Should().Be(2);
        (await CountAsync(database.ConnectionString, "market_resolutions")).Should().Be(1);
        (await CountAsync(database.ConnectionString, "market_resolution_assets")).Should().Be(2);
        (await ReadPayloadsAsync(database.ConnectionString)).Should().BeEquivalentTo(
            payloads,
            options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task ProcessBatch_RepeatedVersionAndNewVersion_ShouldBeIdempotentAndCoexist()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        await SeedRawMessagesAsync(database.ConnectionString, [ReadFixture("last-trade-price.json")]);

        var first = await ProcessAsync(database.ConnectionString, 1, 10);
        var repeated = await ProcessAsync(database.ConnectionString, 1, 10);
        var secondVersion = await ProcessAsync(database.ConnectionString, 2, 10);

        first.Processed.Should().Be(1);
        repeated.Should().BeEquivalentTo(new NormalizationBatchResult(0, 0, 0, 0, 0, null, null));
        secondVersion.Processed.Should().Be(1);
        (await QueryIntsAsync(
            database.ConnectionString,
            "SELECT projection_version FROM data_collection.normalized_events ORDER BY projection_version"))
            .Should().Equal(1, 2);
        (await QueryIntsAsync(
            database.ConnectionString,
            "SELECT projection_version FROM data_collection.raw_message_normalizations ORDER BY projection_version"))
            .Should().Equal(1, 2);
        (await CountAsync(database.ConnectionString, "last_trade_price")).Should().Be(2);
    }

    [Fact]
    public async Task ProcessBatch_InvalidUnsupportedAndEmptyArray_ShouldPersistConsistentOutcomes()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        await SeedRawMessagesAsync(
            database.ConnectionString,
            [
                Encoding.UTF8.GetBytes("{"),
                Encoding.UTF8.GetBytes("{\"event_type\":\"future_event\"}"),
                ReadFixture("empty-array.json")
            ]);

        var result = await ProcessAsync(database.ConnectionString, 1, 10);

        result.Total.Should().Be(3);
        result.Processed.Should().Be(1);
        result.Invalid.Should().Be(1);
        result.Unsupported.Should().Be(1);
        result.Failed.Should().Be(0);
        (await QueryIntsAsync(
            database.ConnectionString,
            "SELECT status FROM data_collection.raw_message_normalizations ORDER BY raw_message_id"))
            .Should().Equal(
                (int)NormalizationStatus.Invalid,
                (int)NormalizationStatus.Unsupported,
                (int)NormalizationStatus.Processed);
        (await QueryStringsAsync(
            database.ConnectionString,
            """
            SELECT error_code
            FROM data_collection.raw_message_normalizations
            WHERE error_code IS NOT NULL
            ORDER BY raw_message_id
            """))
            .Should().Equal(
                "normalization.payload.json.invalid",
                "normalization.event_type.unsupported");
        (await CountAsync(database.ConnectionString, "normalized_events")).Should().Be(0);
    }

    [Fact]
    public async Task ProcessBatch_RootArray_ShouldPersistAllItemsAtomically()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        await SeedRawMessagesAsync(database.ConnectionString, [ReadFixture("book-array.json")]);

        var result = await ProcessAsync(database.ConnectionString, 1, 10);

        result.Processed.Should().Be(1);
        (await QueryIntsAsync(
            database.ConnectionString,
            "SELECT raw_item_index FROM data_collection.normalized_events ORDER BY raw_item_index"))
            .Should().Equal(0, 1);
        (await CountAsync(database.ConnectionString, "book_snapshots")).Should().Be(2);
        (await CountAsync(database.ConnectionString, "book_levels")).Should().BeGreaterThan(0);
        (await QueryIntsAsync(
            database.ConnectionString,
            "SELECT status FROM data_collection.raw_message_normalizations"))
            .Should().Equal((int)NormalizationStatus.Processed);
    }

    [Fact]
    public async Task ProcessBatch_TypedRowFailure_ShouldRollbackMessageAndContinueBatch()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        await SeedRawMessagesAsync(
            database.ConnectionString,
            [
                ReadFixture("last-trade-price.json"),
                ReadFixture("best-bid-ask.json")
            ]);
        await ExecuteAsync(
            database.ConnectionString,
            """
            ALTER TABLE data_collection.last_trade_price
            ADD CONSTRAINT ck_test_reject_last_trade_price CHECK (price < 0)
            """);

        var result = await ProcessAsync(database.ConnectionString, 1, 10);

        result.Should().BeEquivalentTo(new NormalizationBatchResult(
            2,
            1,
            0,
            0,
            1,
            result.FirstRawMessageId,
            result.LastRawMessageId,
            result.Errors));
        (await QueryIntsAsync(
            database.ConnectionString,
            "SELECT status FROM data_collection.raw_message_normalizations ORDER BY raw_message_id"))
            .Should().Equal(
                (int)NormalizationStatus.Failed,
                (int)NormalizationStatus.Processed);
        (await QueryStringsAsync(
            database.ConnectionString,
            """
            SELECT error_code
            FROM data_collection.raw_message_normalizations
            WHERE status = 6
            """))
            .Should().Equal("normalization.processing.failed");
        (await CountAsync(database.ConnectionString, "normalized_events")).Should().Be(1);
        (await CountAsync(database.ConnectionString, "last_trade_price")).Should().Be(0);
        (await CountAsync(database.ConnectionString, "best_bid_asks")).Should().Be(1);
    }

    [Fact]
    public async Task ProcessBatch_ConcurrentProcessors_ShouldClaimDisjointMessages()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var payload = ReadFixture("last-trade-price.json");
        await SeedRawMessagesAsync(
            database.ConnectionString,
            Enumerable.Repeat(payload, 20).ToArray());
        await using var firstContext = CreateContext(database.ConnectionString);
        await using var secondContext = CreateContext(database.ConnectionString);
        using var blockingDecoder = new BlockingDecoder(new RawMessageDecoder());
        var firstProcessor = CreateProcessor(firstContext, 1, 10, blockingDecoder);
        var secondProcessor = CreateProcessor(secondContext, 1, 10);

        var firstTask = firstProcessor.ProcessBatchAsync(default);
        await blockingDecoder.FirstDecodeStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        NormalizationBatchResult secondResult;
        try
        {
            secondResult = await secondProcessor.ProcessBatchAsync(default);
        }
        finally
        {
            blockingDecoder.Release();
        }

        var firstResult = await firstTask;
        var results = new[] { firstResult, secondResult };

        results.Sum(result => result.Total).Should().Be(20);
        results.Sum(result => result.Processed).Should().Be(20);
        results.Sum(result => result.Failed).Should().Be(0);
        results.Should().OnlyContain(result => result.Total == 10);
        (await CountAsync(database.ConnectionString, "raw_message_normalizations")).Should().Be(20);
        (await CountAsync(database.ConnectionString, "normalized_events")).Should().Be(20);
        (await CountAsync(database.ConnectionString, "last_trade_price")).Should().Be(20);
        (await ExecuteScalarAsync<int>(
            database.ConnectionString,
            """
            SELECT count(*)::integer
            FROM (
                SELECT raw_message_id
                FROM data_collection.normalized_events
                GROUP BY raw_message_id
                HAVING count(*) > 1
            ) duplicates
            """)).Should().Be(0);
    }

    private async Task<PostgreSqlTestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await fixture.CreateDatabaseAsync();
        try
        {
            await using var context = CreateContext(database.ConnectionString);
            await context.Database.MigrateAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    private static async Task<NormalizationBatchResult> ProcessAsync(
        string connectionString,
        int projectionVersion,
        int batchSize)
    {
        await using var context = CreateContext(connectionString);
        return await CreateProcessor(context, projectionVersion, batchSize)
            .ProcessBatchAsync(default);
    }

    private static NormalizationProcessor CreateProcessor(
        DataCollectionDbContext context,
        int projectionVersion,
        int batchSize,
        IRawMessageDecoder? decoder = null) =>
        new(
            new RawMessageNormalizationClaimRepository(context),
            decoder ?? new RawMessageDecoder(),
            new NormalizationDispatcher(
            [
                new LastTradePriceNormalizer(),
                new PriceChangeNormalizer(),
                new BookNormalizer(),
                new TickSizeChangeNormalizer(),
                new BestBidAskNormalizer(),
                new NewMarketNormalizer(),
                new MarketResolvedNormalizer()
            ]),
            new VersionedNormalizedWriter(context, TimeProvider.System),
            projectionVersion,
            batchSize,
            ClaimTimeout);

    private static DataCollectionDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<DataCollectionDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new DataCollectionDbContext(options);
    }

    private static async Task<IReadOnlyList<long>> SeedRawMessagesAsync(
        string connectionString,
        IReadOnlyCollection<byte[]> payloads)
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

        var ids = new List<long>(payloads.Count);
        var index = 0;
        foreach (var payload in payloads)
        {
            ids.Add(await ExecuteScalarAsync<long>(
                connectionString,
                """
                INSERT INTO data_collection.raw_market_messages
                    (session_id, received_at, payload)
                VALUES (@session_id, @received_at, @payload)
                RETURNING id
                """,
                new NpgsqlParameter("session_id", sessionId),
                new NpgsqlParameter("received_at", receivedAt.AddMilliseconds(index++)),
                new NpgsqlParameter("payload", payload)));
        }

        return ids;
    }

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

    private static Task<int> CountAsync(string connectionString, string table) =>
        ExecuteScalarAsync<int>(
            connectionString,
            $"SELECT count(*)::integer FROM data_collection.{table}");

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

    private static async Task<IReadOnlyList<string>> QueryStringsAsync(
        string connectionString,
        string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
            values.Add(reader.GetString(0));
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

    private sealed class BlockingDecoder(IRawMessageDecoder inner) : IRawMessageDecoder, IDisposable
    {
        private readonly ManualResetEventSlim release = new(false);
        private int calls;

        public TaskCompletionSource FirstDecodeStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RawMessageDecodeResult Decode(RawMessageEnvelope message)
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                FirstDecodeStarted.TrySetResult();
                release.Wait();
            }

            return inner.Decode(message);
        }

        public void Release() => release.Set();

        public void Dispose() => release.Dispose();
    }
}
