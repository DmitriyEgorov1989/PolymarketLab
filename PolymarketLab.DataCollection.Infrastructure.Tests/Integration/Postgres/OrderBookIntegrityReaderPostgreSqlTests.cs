using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Normalization;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Integration.Postgres;

[Collection(PostgreSqlCollection.Name)]
public sealed class OrderBookIntegrityReaderPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset ReceivedAt =
        DateTimeOffset.Parse("2026-09-03T12:04:00Z");

    [Fact]
    public async Task ReadAsync_ShouldMapTypedRowsInArchiveOrderAndApplySessionVersionScope()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var sessionId = await InsertSessionAsync(database.ConnectionString);
        var otherSessionId = await InsertSessionAsync(database.ConnectionString);
        var raw1 = await InsertRawAsync(database.ConnectionString, sessionId, ReceivedAt);
        var raw2 = await InsertRawAsync(
            database.ConnectionString,
            sessionId,
            ReceivedAt.AddSeconds(1));
        var otherRaw = await InsertRawAsync(
            database.ConnectionString,
            otherSessionId,
            ReceivedAt);

        var book = await InsertEventAsync(
            database.ConnectionString, raw1, 2, 3, "book", sessionId,
            sourceTimestamp: 100, marketConditionId: "condition", assetId: "asset");
        await ExecuteAsync(
            database.ConnectionString,
            "INSERT INTO data_collection.book_snapshots (event_id, hash, tick_size) VALUES (@id, 'hash', 0.01)",
            new NpgsqlParameter("id", book));
        await ExecuteAsync(
            database.ConnectionString,
            """
            INSERT INTO data_collection.book_levels
                (event_id, side, level_index, price, size)
            VALUES (@id, 1, 0, 0.40, 10), (@id, 2, 0, 0.60, 11)
            """,
            new NpgsqlParameter("id", book));

        var priceChange = await InsertEventAsync(
            database.ConnectionString, raw1, 3, 3, "price_change", sessionId,
            sourceTimestamp: 101, marketConditionId: "condition", assetId: null);
        await ExecuteAsync(
            database.ConnectionString,
            """
            INSERT INTO data_collection.price_change
                (event_id, item_index, asset_id, source_timestamp, price, size,
                 side, hash, best_bid, best_ask)
            VALUES (@id, 1, 'asset', 101, 0.60, 0, 2, 'hash-2', 0.40, NULL),
                   (@id, 0, 'asset', 101, 0.41, 12, 1, 'hash-1', 0.41, 0.60)
            """,
            new NpgsqlParameter("id", priceChange));

        var tick = await InsertEventAsync(
            database.ConnectionString, raw2, 0, 3, "tick_size_change", sessionId,
            sourceTimestamp: 102, marketConditionId: "condition", assetId: "asset");
        await ExecuteAsync(
            database.ConnectionString,
            "INSERT INTO data_collection.tick_size_changes (event_id, old_tick_size, new_tick_size) VALUES (@id, 0.01, 0.001)",
            new NpgsqlParameter("id", tick));

        var quote = await InsertEventAsync(
            database.ConnectionString, raw2, 1, 3, "best_bid_ask", sessionId,
            sourceTimestamp: 103, marketConditionId: "condition", assetId: "asset");
        await ExecuteAsync(
            database.ConnectionString,
            "INSERT INTO data_collection.best_bid_asks (event_id, best_bid, best_ask, spread) VALUES (@id, 0.41, 0.62, 0.21)",
            new NpgsqlParameter("id", quote));

        await InsertEventAsync(
            database.ConnectionString, raw1, 4, 2, "book", sessionId,
            sourceTimestamp: 104, marketConditionId: "wrong-version", assetId: "ignored");
        await InsertEventAsync(
            database.ConnectionString, otherRaw, 0, 3, "book", otherSessionId,
            sourceTimestamp: 100, marketConditionId: "other-session", assetId: "ignored");

        await using var context = CreateContext(database.ConnectionString);
        var reader = new OrderBookIntegrityReader(context);

        var events = await reader.ReadAsync(
            CollectorSessionId.Create(sessionId).Value,
            3,
            CancellationToken.None);

        events.Should().HaveCount(4);
        var snapshot = events[0].Should().BeOfType<NormalizedOrderBookEvent.BookSnapshot>()
            .Subject.Record;
        snapshot.Position.Should().Be(new OrderBookEventPosition(raw1, 2, book));
        snapshot.Bids.Should().ContainSingle(level =>
            level.Side == OrderBookSide.Bid && level.Price == 0.40m && level.Size == 10m);
        snapshot.Asks.Should().ContainSingle(level =>
            level.Side == OrderBookSide.Ask && level.Price == 0.60m && level.Size == 11m);

        var changes = events[1].Should().BeOfType<NormalizedOrderBookEvent.PriceChanges>()
            .Subject.Records;
        changes.Select(change => change.ItemIndex).Should().Equal(0, 1);
        changes.Should().OnlyContain(change =>
            change.Position == new OrderBookEventPosition(raw1, 3, priceChange));

        var tickRecord = events[2]
            .Should().BeOfType<NormalizedOrderBookEvent.TickSizeChange>().Subject.Record;
        tickRecord.Position.Should().Be(new OrderBookEventPosition(raw2, 0, tick));
        tickRecord.NewTickSize.Should().Be(0.001m);

        var quoteRecord = events[3]
            .Should().BeOfType<NormalizedOrderBookEvent.BestBidAsk>().Subject.Record;
        quoteRecord.Position.Should().Be(new OrderBookEventPosition(raw2, 1, quote));
        quoteRecord.Spread.Should().Be(0.21m);
    }

    private async Task<PostgreSqlTestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await fixture.CreateDatabaseAsync();
        await using var context = CreateContext(database.ConnectionString);
        await context.Database.MigrateAsync();
        return database;
    }

    private static DataCollectionDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<DataCollectionDbContext>()
            .UseNpgsql(connectionString)
            .Options);

    private static async Task<Guid> InsertSessionAsync(string connectionString)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(
            connectionString,
            "INSERT INTO data_collection.collector_sessions (id, market_id, status, created_at) VALUES (@id, @market, 4, @created)",
            new NpgsqlParameter("id", id),
            new NpgsqlParameter("market", Guid.NewGuid()),
            new NpgsqlParameter("created", ReceivedAt));
        return id;
    }

    private static Task<long> InsertRawAsync(
        string connectionString,
        Guid sessionId,
        DateTimeOffset receivedAt) =>
        ExecuteScalarAsync<long>(
            connectionString,
            "INSERT INTO data_collection.raw_market_messages (session_id, connection_epoch, received_at, payload) VALUES (@session, 1, @received, @payload) RETURNING id",
            new NpgsqlParameter("session", sessionId),
            new NpgsqlParameter("received", receivedAt),
            new NpgsqlParameter("payload", new byte[] { 1 }));

    private static Task<long> InsertEventAsync(
        string connectionString,
        long rawMessageId,
        int rawItemIndex,
        int projectionVersion,
        string eventType,
        Guid sessionId,
        long? sourceTimestamp,
        string? marketConditionId,
        string? assetId) =>
        ExecuteScalarAsync<long>(
            connectionString,
            """
            INSERT INTO data_collection.normalized_events
                (raw_message_id, raw_item_index, projection_version, normalizer_version,
                 event_type, session_id, received_at, source_timestamp,
                 market_condition_id, asset_id, normalized_at)
            VALUES (@raw, @item, @version, 1, @type, @session, @received,
                    @timestamp, @market, @asset, @normalized)
            RETURNING id
            """,
            new NpgsqlParameter("raw", rawMessageId),
            new NpgsqlParameter("item", rawItemIndex),
            new NpgsqlParameter("version", projectionVersion),
            new NpgsqlParameter("type", eventType),
            new NpgsqlParameter("session", sessionId),
            new NpgsqlParameter("received", ReceivedAt),
            new NpgsqlParameter("timestamp", (object?)sourceTimestamp ?? DBNull.Value),
            new NpgsqlParameter("market", (object?)marketConditionId ?? DBNull.Value),
            new NpgsqlParameter("asset", (object?)assetId ?? DBNull.Value),
            new NpgsqlParameter("normalized", ReceivedAt.AddSeconds(1)));

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
