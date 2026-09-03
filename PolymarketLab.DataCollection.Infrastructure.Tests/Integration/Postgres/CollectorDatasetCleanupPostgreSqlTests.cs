using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
public sealed class CollectorDatasetCleanupPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset CreatedAt =
        DateTimeOffset.Parse("2026-09-03T10:00:00Z");
    private static readonly DateTimeOffset CompletedAt =
        DateTimeOffset.Parse("2026-09-03T10:01:00Z");

    [Fact]
    public async Task CleanupAsync_ShouldDeleteOnlyTargetDatasetAndPersistAudit()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var unrelated = await InsertSessionAsync(database.ConnectionString, terminal: true);
        var target = await InsertSessionAsync(database.ConnectionString, terminal: false);
        await SeedDatasetAsync(database.ConnectionString, unrelated.Id);
        await SeedDatasetAsync(database.ConnectionString, target.Id);
        await using var staleContext = CreateContext(database.ConnectionString);
        var staleTarget = await new CollectorSessionRepository(staleContext)
            .GetByIdAsync(target.Id, CancellationToken.None);

        await using var context = CreateContext(database.ConnectionString);
        var cleanup = new CollectorDatasetCleanup(
            context,
            new FixedTimeProvider(CompletedAt));

        var result = await cleanup.CleanupAsync(target, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(new
        {
            SessionId = target.Id,
            CompletedAt,
            DeletedRawMessageCount = 1L,
            DeletedNormalizationCount = 2L,
            DeletedNormalizedEventCount = 14L
        });
        (await ReadCountAsync(database.ConnectionString, "raw_market_messages", target.Id))
            .Should().Be(0);
        (await ReadCountAsync(database.ConnectionString, "raw_market_messages", unrelated.Id))
            .Should().Be(1);
        (await ReadOwnedByRawCountAsync(
            database.ConnectionString,
            "raw_message_normalizations",
            target.Id)).Should().Be(0);
        (await ReadOwnedByRawCountAsync(
            database.ConnectionString,
            "raw_message_normalizations",
            unrelated.Id)).Should().Be(2);
        (await ReadCountAsync(database.ConnectionString, "normalized_events", target.Id))
            .Should().Be(0);
        (await ReadCountAsync(database.ConnectionString, "normalized_events", unrelated.Id))
            .Should().Be(14);
        (await ReadCountAsync(
            database.ConnectionString,
            "collector_dataset_cleanup_audits",
            target.Id)).Should().Be(1);
        foreach (var table in TypedTables)
        {
            (await ReadTotalCountAsync(database.ConnectionString, table))
                .Should().Be(2, $"typed table {table} must retain only the unrelated session");
        }
        (await ReadProgressCounterAsync(database.ConnectionString, target.Id))
            .Should().Be(1);

        await using var verificationContext = CreateContext(database.ConnectionString);
        var persisted = await new CollectorSessionRepository(verificationContext)
            .GetByIdAsync(target.Id, CancellationToken.None);
        persisted!.Status.Should().Be(CollectorSessionStatus.Failed);
        persisted.Phase.Should().BeNull();
        persisted.InvalidatingAt.Should().Be(CreatedAt.AddSeconds(30));
        persisted.FailureCode.Should().Be("collector.test.failure");

        await using var retryContext = CreateContext(database.ConnectionString);
        var retry = await new CollectorDatasetCleanup(
                retryContext,
                new FixedTimeProvider(CompletedAt.AddMinutes(1)))
            .CleanupAsync(staleTarget!, CancellationToken.None);
        retry.IsSuccess.Should().BeTrue();
        retry.Value.Should().Be(result.Value);
        staleTarget!.Status.Should().Be(CollectorSessionStatus.Failed);
    }

    [Fact]
    public async Task CleanupAsync_WhenTransactionFails_ShouldRollbackEntireDataset()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var target = await InsertSessionAsync(database.ConnectionString, terminal: false);
        await SeedDatasetAsync(database.ConnectionString, target.Id);
        await CreateDeleteGuardAsync(database.ConnectionString, target.Id);
        await using var context = CreateContext(database.ConnectionString);
        var cleanup = new CollectorDatasetCleanup(
            context,
            new FixedTimeProvider(CompletedAt));

        var action = () => cleanup.CleanupAsync(target, CancellationToken.None);

        await action.Should().ThrowAsync<PostgresException>();
        (await ReadCountAsync(database.ConnectionString, "raw_market_messages", target.Id))
            .Should().Be(1);
        (await ReadOwnedByRawCountAsync(
            database.ConnectionString,
            "raw_message_normalizations",
            target.Id)).Should().Be(2);
        (await ReadCountAsync(database.ConnectionString, "normalized_events", target.Id))
            .Should().Be(14);
        (await ReadCountAsync(
            database.ConnectionString,
            "collector_dataset_cleanup_audits",
            target.Id)).Should().Be(0);
        await using var verificationContext = CreateContext(database.ConnectionString);
        var persisted = await new CollectorSessionRepository(verificationContext)
            .GetByIdAsync(target.Id, CancellationToken.None);
        persisted!.Status.Should().Be(CollectorSessionStatus.Invalidating);
        target.Status.Should().Be(CollectorSessionStatus.Invalidating);
    }

    private static readonly string[] TypedTables =
    [
        "last_trade_price",
        "price_change",
        "book_snapshots",
        "book_levels",
        "tick_size_changes",
        "best_bid_asks",
        "new_markets",
        "new_market_assets",
        "market_resolutions",
        "market_resolution_assets"
    ];

    private async Task<PostgreSqlTestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await fixture.CreateDatabaseAsync();
        await using var context = CreateContext(database.ConnectionString);
        await context.Database.MigrateAsync();
        return database;
    }

    private static async Task<CollectorSessionAggregate> InsertSessionAsync(
        string connectionString,
        bool terminal)
    {
        var session = CollectorSessionAggregate.Create(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            MarketId.Create(Guid.NewGuid()).Value,
            "event-123",
            "btc-updown-5m-1200",
            "market-123",
            "btc-updown-5m-1200",
            "0xabc",
            CreatedAt.AddMinutes(3),
            CreatedAt.AddMinutes(8),
            2,
            [
                new CollectorSessionTokenDefinition(TokenId.Create("1001").Value, "Yes", 0),
                new CollectorSessionTokenDefinition(TokenId.Create("1002").Value, "No", 1)
            ],
            CreatedAt).Value;
        await using var context = CreateContext(connectionString);
        var repository = new CollectorSessionRepository(context);
        (await repository.TryAddAsync(session, CancellationToken.None)).Value
            .Should().Be(CollectorSessionInsertStatus.Inserted);

        if (terminal)
            session.Stop(CreatedAt.AddSeconds(20), CollectorStopReason.MarketClosed);
        else
            session.BeginInvalidation(
                CreatedAt.AddSeconds(30),
                CollectorStopReason.FatalWebSocketError,
                "collector.test.failure",
                "Collector failed during the test.");

        (await repository.TryUpdateAsync(
            session,
            CollectorSessionStatus.Scheduled,
            CancellationToken.None)).Value.Should().Be(CollectorSessionUpdateStatus.Updated);
        return session;
    }

    private static async Task SeedDatasetAsync(
        string connectionString,
        CollectorSessionId sessionId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            WITH raw AS (
                INSERT INTO data_collection.raw_market_messages
                    (session_id, connection_epoch, received_at, payload)
                VALUES (@session_id, 1, @received_at, decode('7b7d', 'hex'))
                RETURNING id
            ), ledger AS (
                INSERT INTO data_collection.raw_message_normalizations
                    (raw_message_id, projection_version, status, attempt_count)
                SELECT id, version, 2, 1
                FROM raw CROSS JOIN (VALUES (1), (2)) AS versions(version)
            )
            INSERT INTO data_collection.normalized_events
                (raw_message_id, raw_item_index, projection_version,
                 normalizer_version, event_type, session_id, received_at, normalized_at)
            SELECT id, (event.ordinality - 1)::integer, version, 1, event.event_type,
                   @session_id, @received_at, @received_at
            FROM raw
            CROSS JOIN (VALUES (1), (2)) AS versions(version)
            CROSS JOIN unnest(ARRAY[
                'last_trade_price', 'price_change', 'book', 'tick_size_change',
                'best_bid_ask', 'new_market', 'market_resolved'
            ]) WITH ORDINALITY AS event(event_type, ordinality);

            INSERT INTO data_collection.last_trade_price
                (event_id, price, side)
            SELECT id, 0.50, 0 FROM data_collection.normalized_events
            WHERE session_id = @session_id AND event_type = 'last_trade_price';

            INSERT INTO data_collection.price_change
                (event_id, item_index, asset_id, price, size, side)
            SELECT id, 0, '1001', 0.50, 1.00, 0 FROM data_collection.normalized_events
            WHERE session_id = @session_id AND event_type = 'price_change';

            INSERT INTO data_collection.book_snapshots (event_id, hash)
            SELECT id, 'hash' FROM data_collection.normalized_events
            WHERE session_id = @session_id AND event_type = 'book';

            INSERT INTO data_collection.book_levels
                (event_id, side, level_index, price, size)
            SELECT id, 0, 0, 0.49, 1.00 FROM data_collection.normalized_events
            WHERE session_id = @session_id AND event_type = 'book';

            INSERT INTO data_collection.tick_size_changes
                (event_id, old_tick_size, new_tick_size)
            SELECT id, 0.01, 0.001 FROM data_collection.normalized_events
            WHERE session_id = @session_id AND event_type = 'tick_size_change';

            INSERT INTO data_collection.best_bid_asks
                (event_id, best_bid, best_ask, spread)
            SELECT id, 0.49, 0.51, 0.02 FROM data_collection.normalized_events
            WHERE session_id = @session_id AND event_type = 'best_bid_ask';

            INSERT INTO data_collection.new_markets
                (event_id, external_market_id, question, slug, description, active,
                 sports_market_type, game_start_time, order_price_min_tick_size,
                 group_item_title, taker_base_fee, fees_enabled, event_message_id,
                 event_message_ticker, event_message_slug, event_message_title,
                 event_message_description, fee_schedule_exponent, fee_schedule_rate,
                 fee_schedule_rebate_rate, fee_schedule_taker_only)
            SELECT id, 'market', 'question', 'slug', 'description', true,
                   '', '', 0.01, '', 0, false, 'event', '', '', '', '', 0, 0, 0, false
            FROM data_collection.normalized_events
            WHERE session_id = @session_id AND event_type = 'new_market';

            INSERT INTO data_collection.new_market_assets
                (event_id, item_index, asset_id, outcome)
            SELECT id, 0, '1001', 'Yes' FROM data_collection.normalized_events
            WHERE session_id = @session_id AND event_type = 'new_market';

            INSERT INTO data_collection.market_resolutions
                (event_id, external_market_id, winning_asset_id, winning_outcome)
            SELECT id, 'market', '1001', 'Yes' FROM data_collection.normalized_events
            WHERE session_id = @session_id AND event_type = 'market_resolved';

            INSERT INTO data_collection.market_resolution_assets
                (event_id, item_index, asset_id)
            SELECT id, 0, '1001' FROM data_collection.normalized_events
            WHERE session_id = @session_id AND event_type = 'market_resolved';

            UPDATE data_collection.collector_session_progress
            SET messages_received = 1,
                messages_enqueued = 1,
                messages_persisted = 1
            WHERE session_id = @session_id;
            """;
        command.Parameters.AddWithValue("session_id", sessionId.Value);
        command.Parameters.AddWithValue("received_at", CreatedAt);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateDeleteGuardAsync(
        string connectionString,
        CollectorSessionId sessionId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE data_collection.cleanup_delete_guard (
                raw_message_id bigint PRIMARY KEY REFERENCES data_collection.raw_market_messages(id)
            );
            INSERT INTO data_collection.cleanup_delete_guard (raw_message_id)
            SELECT id FROM data_collection.raw_market_messages WHERE session_id = @session_id;
            """;
        command.Parameters.AddWithValue("session_id", sessionId.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ReadCountAsync(
        string connectionString,
        string table,
        CollectorSessionId sessionId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM data_collection.{table} WHERE session_id = @session_id";
        command.Parameters.AddWithValue("session_id", sessionId.Value);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> ReadOwnedByRawCountAsync(
        string connectionString,
        string table,
        CollectorSessionId sessionId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COUNT(*)
            FROM data_collection.{table} AS owned
            JOIN data_collection.raw_market_messages AS raw
              ON raw.id = owned.raw_message_id
            WHERE raw.session_id = @session_id
            """;
        command.Parameters.AddWithValue("session_id", sessionId.Value);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> ReadTotalCountAsync(
        string connectionString,
        string table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM data_collection.{table}";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> ReadProgressCounterAsync(
        string connectionString,
        CollectorSessionId sessionId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT messages_persisted
            FROM data_collection.collector_session_progress
            WHERE session_id = @session_id
            """;
        command.Parameters.AddWithValue("session_id", sessionId.Value);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static DataCollectionDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<DataCollectionDbContext>()
            .UseNpgsql(connectionString)
            .Options);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
