using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Integration.Postgres;

[Collection(PostgreSqlCollection.Name)]
public sealed class NormalizationPostgreSqlIntegrationTests(PostgreSqlFixture fixture)
{
    private const string BaselineMigration =
        "20260807081007_AddCollectorSessionProgress";
    private const string NormalizationMigration =
        "20260812103143_AddNormalizationSchema";

    [Fact]
    public async Task Migrations_UpDownUp_ShouldPreserveRawArchive()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = CreateContext(database.ConnectionString);
        var migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync(BaselineMigration);
        var seed = await SeedRawMessageAsync(
            database.ConnectionString,
            includeConnectionEpoch: false);
        var expectedPayload = seed.Payload;

        await migrator.MigrateAsync(NormalizationMigration);

        var migrations = await QueryStringsAsync(
            database.ConnectionString,
            "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"");
        migrations.Should().Contain(NormalizationMigration);
        (await CountNormalizationTablesAsync(database.ConnectionString)).Should().Be(12);
        (await ReadPayloadAsync(database.ConnectionString, seed.RawMessageId))
            .Should().Equal(expectedPayload);

        await migrator.MigrateAsync(BaselineMigration);

        (await CountNormalizationTablesAsync(database.ConnectionString)).Should().Be(0);
        (await ReadPayloadAsync(database.ConnectionString, seed.RawMessageId))
            .Should().Equal(expectedPayload);
        var payloadType = await ExecuteScalarAsync<string>(
            database.ConnectionString,
            """
            SELECT udt_name
            FROM information_schema.columns
            WHERE table_schema = 'data_collection'
              AND table_name = 'raw_market_messages'
              AND column_name = 'payload'
            """);
        payloadType.Should().Be("bytea");

        await migrator.MigrateAsync(NormalizationMigration);
        (await CountNormalizationTablesAsync(database.ConnectionString)).Should().Be(12);
    }

    [Fact]
    public async Task ForeignKeys_ShouldRejectOrphansAndCascadeTypedRows()
    {
        await using var database = await CreateMigratedDatabaseAsync();

        var orphan = async () => await ExecuteAsync(
            database.ConnectionString,
            """
            INSERT INTO data_collection.raw_message_normalizations
                (raw_message_id, projection_version, status, attempt_count)
            VALUES (9223372036854775000, 1, 1, 0)
            """);
        var orphanError = await orphan.Should().ThrowAsync<PostgresException>();
        orphanError.Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);

        var seed = await SeedRawMessageAsync(database.ConnectionString);
        var orphanEvent = async () => await ExecuteAsync(
            database.ConnectionString,
            """
            INSERT INTO data_collection.normalized_events
                (raw_message_id, raw_item_index, projection_version,
                 normalizer_version, event_type, session_id, received_at, normalized_at)
            VALUES
                (9223372036854775000, 0, 1, 1, 'book',
                 @session_id, @received_at, now())
            """,
            new NpgsqlParameter("session_id", seed.SessionId),
            new NpgsqlParameter("received_at", seed.ReceivedAt));
        var orphanEventError = await orphanEvent.Should().ThrowAsync<PostgresException>();
        orphanEventError.Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);

        var eventId = await InsertEventAsync(
            database.ConnectionString,
            seed,
            rawItemIndex: 0,
            projectionVersion: 1,
            eventType: "book");
        await ExecuteAsync(
            database.ConnectionString,
            """
            INSERT INTO data_collection.book_snapshots (event_id, hash)
            VALUES (@event_id, 'snapshot');
            INSERT INTO data_collection.book_levels
                (event_id, side, level_index, price, size)
            VALUES
                (@event_id, 1, 0, 0.4, 10),
                (@event_id, 2, 0, 0.6, 20)
            """,
            new NpgsqlParameter("event_id", eventId));

        // With no ledger row yet, this proves the normalized-events FK restricts raw deletion.
        var deleteRaw = async () => await ExecuteAsync(
            database.ConnectionString,
            "DELETE FROM data_collection.raw_market_messages WHERE id = @id",
            new NpgsqlParameter("id", seed.RawMessageId));
        var restrictError = await deleteRaw.Should().ThrowAsync<PostgresException>();
        restrictError.Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);

        await InsertLedgerAsync(database.ConnectionString, seed.RawMessageId, 1);
        await ExecuteAsync(
            database.ConnectionString,
            "DELETE FROM data_collection.normalized_events WHERE id = @id",
            new NpgsqlParameter("id", eventId));

        (await CountAsync(
            database.ConnectionString,
            "book_snapshots",
            "event_id",
            eventId)).Should().Be(0);
        (await CountAsync(
            database.ConnectionString,
            "book_levels",
            "event_id",
            eventId)).Should().Be(0);
        (await CountAsync(
            database.ConnectionString,
            "raw_message_normalizations",
            "raw_message_id",
            seed.RawMessageId)).Should().Be(1);
    }

    [Fact]
    public async Task UniqueConstraints_ShouldPreserveOrderAndAllowProjectionVersions()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var seed = await SeedRawMessageAsync(database.ConnectionString);

        await InsertLedgerAsync(database.ConnectionString, seed.RawMessageId, 1);
        await InsertLedgerAsync(database.ConnectionString, seed.RawMessageId, 2);
        var duplicateLedger = async () => await InsertLedgerAsync(
            database.ConnectionString,
            seed.RawMessageId,
            1);
        var ledgerError = await duplicateLedger.Should().ThrowAsync<PostgresException>();
        ledgerError.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        ledgerError.Which.ConstraintName.Should().Be("PK_raw_message_normalizations");

        var eventV1 = await InsertEventAsync(
            database.ConnectionString,
            seed,
            rawItemIndex: 0,
            projectionVersion: 1,
            eventType: "price_change");
        await InsertEventAsync(
            database.ConnectionString,
            seed,
            rawItemIndex: 0,
            projectionVersion: 2,
            eventType: "price_change");

        var duplicateEvent = async () => await InsertEventAsync(
            database.ConnectionString,
            seed,
            rawItemIndex: 0,
            projectionVersion: 1,
            eventType: "price_change");
        var eventError = await duplicateEvent.Should().ThrowAsync<PostgresException>();
        eventError.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        eventError.Which.ConstraintName.Should().Be(
            "ux_normalized_events_raw_message_item_projection");

        await ExecuteAsync(
            database.ConnectionString,
            """
            INSERT INTO data_collection.price_change
                (event_id, item_index, asset_id, source_timestamp, price, size, side)
            VALUES
                (@event_id, 0, 'asset-a', 1000, 0.4, 1, 1),
                (@event_id, 1, 'asset-b', 1001, 0.5, 2, 2)
            """,
            new NpgsqlParameter("event_id", eventV1));

        var duplicateItem = async () => await ExecuteAsync(
            database.ConnectionString,
            """
            INSERT INTO data_collection.price_change
                (event_id, item_index, asset_id, price, size, side)
            VALUES (@event_id, 1, 'asset-c', 0.6, 3, 1)
            """,
            new NpgsqlParameter("event_id", eventV1));
        var itemError = await duplicateItem.Should().ThrowAsync<PostgresException>();
        itemError.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        itemError.Which.ConstraintName.Should().Be(
            "ux_price_change_event_id_item_index");

        await ExecuteAsync(
            database.ConnectionString,
            """
            INSERT INTO data_collection.book_snapshots (event_id, hash)
            VALUES (@event_id, 'snapshot');
            INSERT INTO data_collection.book_levels
                (event_id, side, level_index, price, size)
            VALUES
                (@event_id, 1, 0, 0.4, 1),
                (@event_id, 2, 0, 0.6, 1)
            """,
            new NpgsqlParameter("event_id", eventV1));
        var duplicateLevel = async () => await ExecuteAsync(
            database.ConnectionString,
            """
            INSERT INTO data_collection.book_levels
                (event_id, side, level_index, price, size)
            VALUES (@event_id, 1, 0, 0.5, 1)
            """,
            new NpgsqlParameter("event_id", eventV1));
        var levelError = await duplicateLevel.Should().ThrowAsync<PostgresException>();
        levelError.Which.ConstraintName.Should().Be(
            "ux_book_levels_event_side_level_index");

        await ExecuteAsync(
            database.ConnectionString,
            """
            INSERT INTO data_collection.market_resolutions
                (event_id, external_market_id, winning_asset_id, winning_outcome)
            VALUES (@event_id, 'market', 'winner', 'Yes');
            INSERT INTO data_collection.market_resolution_assets
                (event_id, item_index, asset_id)
            VALUES (@event_id, 0, 'asset-a')
            """,
            new NpgsqlParameter("event_id", eventV1));
        var duplicateResolutionAsset = async () => await ExecuteAsync(
            database.ConnectionString,
            """
            INSERT INTO data_collection.market_resolution_assets
                (event_id, item_index, asset_id)
            VALUES (@event_id, 0, 'asset-b')
            """,
            new NpgsqlParameter("event_id", eventV1));
        var resolutionError = await duplicateResolutionAsset
            .Should().ThrowAsync<PostgresException>();
        resolutionError.Which.ConstraintName.Should().Be(
            "ux_market_resolution_assets_event_id_item_index");

        await ExecuteAsync(
            database.ConnectionString,
            """
            INSERT INTO data_collection.new_markets
                (event_id, external_market_id, question, slug, description, active,
                 sports_market_type, game_start_time, order_price_min_tick_size,
                 group_item_title, taker_base_fee, fees_enabled,
                 event_message_id, event_message_ticker, event_message_slug,
                 event_message_title, event_message_description,
                 fee_schedule_exponent, fee_schedule_rate,
                 fee_schedule_rebate_rate, fee_schedule_taker_only)
            VALUES
                (@event_id, 'market', 'question', 'slug', 'description', true,
                 '', '', 0.01, '', 0, false,
                 'event', 'ticker', 'event-slug', 'title', 'description',
                 0, 0, 0, false);
            INSERT INTO data_collection.new_market_assets
                (event_id, item_index, asset_id, outcome)
            VALUES (@event_id, 0, 'asset-a', 'Yes')
            """,
            new NpgsqlParameter("event_id", eventV1));
        var duplicateMarketAsset = async () => await ExecuteAsync(
            database.ConnectionString,
            """
            INSERT INTO data_collection.new_market_assets
                (event_id, item_index, asset_id, outcome)
            VALUES (@event_id, 0, 'asset-b', 'No')
            """,
            new NpgsqlParameter("event_id", eventV1));
        var marketAssetError = await duplicateMarketAsset
            .Should().ThrowAsync<PostgresException>();
        marketAssetError.Which.ConstraintName.Should().Be(
            "ux_new_market_assets_event_id_item_index");

        var versions = await QueryIntsAsync(
            database.ConnectionString,
            """
            SELECT projection_version
            FROM data_collection.normalized_events
            WHERE raw_message_id = @raw_message_id AND raw_item_index = 0
            ORDER BY projection_version
            """,
            new NpgsqlParameter("raw_message_id", seed.RawMessageId));
        versions.Should().Equal(1, 2);
    }

    [Fact]
    public async Task NumericColumns_ShouldRoundTripPrecisionAndRejectOverflow()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var seed = await SeedRawMessageAsync(database.ConnectionString);
        var eventId = await InsertEventAsync(
            database.ConnectionString,
            seed,
            rawItemIndex: 0,
            projectionVersion: 1,
            eventType: "last_trade_price");

        await ExecuteAsync(
            database.ConnectionString,
            """
            INSERT INTO data_collection.last_trade_price
                (event_id, price, size, side, fee_rate_bps)
            VALUES
                (@event_id,
                 0.1234567890123456789012345678,
                 12345678901.123456789012345678,
                 1,
                 12.000000000000000001)
            """,
            new NpgsqlParameter("event_id", eventId));

        var values = await QueryStringsAsync(
            database.ConnectionString,
            """
            SELECT price::text, size::text, fee_rate_bps::text
            FROM data_collection.last_trade_price
            WHERE event_id = @event_id
            """,
            new NpgsqlParameter("event_id", eventId));
        values.Should().Equal(
            "0.1234567890123456789012345678",
            "12345678901.123456789012345678",
            "12.000000000000000001");

        var precision = await QueryStringsAsync(
            database.ConnectionString,
            """
            SELECT numeric_precision::text || ':' || numeric_scale::text
            FROM information_schema.columns
            WHERE table_schema = 'data_collection'
              AND table_name = 'last_trade_price'
              AND column_name IN ('price', 'size', 'fee_rate_bps')
            ORDER BY column_name
            """);
        precision.Should().BeEquivalentTo("29:28", "29:18", "29:18");

        var numericColumnCount = await ExecuteScalarAsync<int>(
            database.ConnectionString,
            """
            SELECT count(*)::integer
            FROM information_schema.columns
            WHERE table_schema = 'data_collection' AND data_type = 'numeric'
            """);
        numericColumnCount.Should().Be(23);
        var invalidNumericProfiles = await ExecuteScalarAsync<int>(
            database.ConnectionString,
            """
            SELECT count(*)::integer
            FROM information_schema.columns
            WHERE table_schema = 'data_collection'
              AND data_type = 'numeric'
              AND (numeric_precision, numeric_scale) NOT IN ((29, 28), (29, 18))
            """);
        invalidNumericProfiles.Should().Be(0);

        var overflow = async () => await ExecuteAsync(
            database.ConnectionString,
            "UPDATE data_collection.last_trade_price SET price = 10 WHERE event_id = @id",
            new NpgsqlParameter("id", eventId));
        var overflowError = await overflow.Should().ThrowAsync<PostgresException>();
        overflowError.Which.SqlState.Should().Be(PostgresErrorCodes.NumericValueOutOfRange);
    }

    [Fact]
    public async Task FailedNormalizationTransaction_ShouldRollbackProjectionRows()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var seed = await SeedRawMessageAsync(database.ConnectionString);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO data_collection.raw_message_normalizations
                (raw_message_id, projection_version, status, attempt_count)
            VALUES (@raw_id, 1, 2, 1)
            """,
            new NpgsqlParameter("raw_id", seed.RawMessageId));
        var eventId = await ExecuteScalarAsync<long>(
            connection,
            transaction,
            """
            INSERT INTO data_collection.normalized_events
                (raw_message_id, raw_item_index, projection_version,
                 normalizer_version, event_type, session_id, received_at, normalized_at)
            VALUES (@raw_id, 0, 1, 1, 'last_trade_price', @session_id, @received_at, now())
            RETURNING id
            """,
            new NpgsqlParameter("raw_id", seed.RawMessageId),
            new NpgsqlParameter("session_id", seed.SessionId),
            new NpgsqlParameter("received_at", seed.ReceivedAt));
        await ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO data_collection.last_trade_price (event_id, price, side)
            VALUES (@event_id, 0.5, 1)
            """,
            new NpgsqlParameter("event_id", eventId));

        var duplicate = async () => await ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO data_collection.last_trade_price (event_id, price, side)
            VALUES (@event_id, 0.6, 2)
            """,
            new NpgsqlParameter("event_id", eventId));
        var error = await duplicate.Should().ThrowAsync<PostgresException>();
        error.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        await transaction.RollbackAsync();

        (await CountAsync(
            database.ConnectionString,
            "raw_message_normalizations",
            "raw_message_id",
            seed.RawMessageId)).Should().Be(0);
        (await CountAsync(
            database.ConnectionString,
            "normalized_events",
            "raw_message_id",
            seed.RawMessageId)).Should().Be(0);
        (await CountAsync(
            database.ConnectionString,
            "raw_market_messages",
            "id",
            seed.RawMessageId)).Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentProjectionInsert_ShouldHaveSingleWinner()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var seed = await SeedRawMessageAsync(database.ConnectionString);
        await using var firstConnection = new NpgsqlConnection(database.ConnectionString);
        await using var secondConnection = new NpgsqlConnection(database.ConnectionString);
        await firstConnection.OpenAsync();
        await secondConnection.OpenAsync();
        await using var firstTransaction = await firstConnection.BeginTransactionAsync();
        await using var secondTransaction = await secondConnection.BeginTransactionAsync();

        await InsertEventAsync(firstConnection, firstTransaction, seed, 0, 1, "book");
        var secondInsert = InsertEventAsync(
            secondConnection,
            secondTransaction,
            seed,
            0,
            1,
            "book");

        await Task.Delay(100);
        secondInsert.IsCompleted.Should().BeFalse();
        await firstTransaction.CommitAsync();

        var conflict = await FluentActions.Awaiting(() => secondInsert)
            .Should().ThrowAsync<PostgresException>();
        conflict.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        conflict.Which.ConstraintName.Should().Be(
            "ux_normalized_events_raw_message_item_projection");
        await secondTransaction.RollbackAsync();

        (await CountAsync(
            database.ConnectionString,
            "normalized_events",
            "raw_message_id",
            seed.RawMessageId)).Should().Be(1);
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

    private static DataCollectionDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<DataCollectionDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new DataCollectionDbContext(options);
    }

    private static async Task<RawSeed> SeedRawMessageAsync(
        string connectionString,
        bool includeConnectionEpoch = true)
    {
        var sessionId = Guid.NewGuid();
        var marketId = Guid.NewGuid();
        var receivedAt = DateTimeOffset.Parse("2026-08-12T10:01:00Z");
        var payload = new byte[] { 0, 1, 2, 255, 10 };

        await ExecuteAsync(
            connectionString,
            """
            INSERT INTO data_collection.collector_sessions
                (id, market_id, status, created_at)
            VALUES (@session_id, @market_id, 4, @created_at)
            """,
            new NpgsqlParameter("session_id", sessionId),
            new NpgsqlParameter("market_id", marketId),
            new NpgsqlParameter("created_at", receivedAt.AddMinutes(-1)));
        var rawMessageId = await ExecuteScalarAsync<long>(
            connectionString,
            includeConnectionEpoch
                ? """
                  INSERT INTO data_collection.raw_market_messages
                      (session_id, connection_epoch, received_at, payload)
                  VALUES (@session_id, 1, @received_at, @payload)
                  RETURNING id
                  """
                : """
                  INSERT INTO data_collection.raw_market_messages
                      (session_id, received_at, payload)
                  VALUES (@session_id, @received_at, @payload)
                  RETURNING id
                  """,
            new NpgsqlParameter("session_id", sessionId),
            new NpgsqlParameter("received_at", receivedAt),
            new NpgsqlParameter("payload", payload));

        return new RawSeed(sessionId, rawMessageId, receivedAt, payload);
    }

    private static Task InsertLedgerAsync(
        string connectionString,
        long rawMessageId,
        int projectionVersion) =>
        ExecuteAsync(
            connectionString,
            """
            INSERT INTO data_collection.raw_message_normalizations
                (raw_message_id, projection_version, status, attempt_count)
            VALUES (@raw_id, @projection_version, 1, 0)
            """,
            new NpgsqlParameter("raw_id", rawMessageId),
            new NpgsqlParameter("projection_version", projectionVersion));

    private static Task<long> InsertEventAsync(
        string connectionString,
        RawSeed seed,
        int rawItemIndex,
        int projectionVersion,
        string eventType) =>
        ExecuteScalarAsync<long>(
            connectionString,
            EventInsertSql,
            EventParameters(seed, rawItemIndex, projectionVersion, eventType));

    private static Task<long> InsertEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        RawSeed seed,
        int rawItemIndex,
        int projectionVersion,
        string eventType) =>
        ExecuteScalarAsync<long>(
            connection,
            transaction,
            EventInsertSql,
            EventParameters(seed, rawItemIndex, projectionVersion, eventType));

    private const string EventInsertSql =
        """
        INSERT INTO data_collection.normalized_events
            (raw_message_id, raw_item_index, projection_version,
             normalizer_version, event_type, session_id, received_at, normalized_at)
        VALUES
            (@raw_id, @raw_item_index, @projection_version,
             1, @event_type, @session_id, @received_at, now())
        RETURNING id
        """;

    private static NpgsqlParameter[] EventParameters(
        RawSeed seed,
        int rawItemIndex,
        int projectionVersion,
        string eventType) =>
        [
            new("raw_id", seed.RawMessageId),
            new("raw_item_index", rawItemIndex),
            new("projection_version", projectionVersion),
            new("event_type", eventType),
            new("session_id", seed.SessionId),
            new("received_at", seed.ReceivedAt)
        ];

    private static async Task<byte[]> ReadPayloadAsync(
        string connectionString,
        long rawMessageId) =>
        await ExecuteScalarAsync<byte[]>(
            connectionString,
            "SELECT payload FROM data_collection.raw_market_messages WHERE id = @id",
            new NpgsqlParameter("id", rawMessageId));

    private static async Task<int> CountNormalizationTablesAsync(string connectionString) =>
        await ExecuteScalarAsync<int>(
            connectionString,
            """
            SELECT count(*)::integer
            FROM information_schema.tables
            WHERE table_schema = 'data_collection'
              AND table_name IN (
                  'raw_message_normalizations', 'normalized_events',
                  'last_trade_price', 'price_change',
                  'book_snapshots', 'book_levels',
                  'tick_size_changes', 'best_bid_asks',
                  'new_markets', 'new_market_assets',
                  'market_resolutions', 'market_resolution_assets')
            """);

    private static async Task<int> CountAsync(
        string connectionString,
        string table,
        string column,
        long value) =>
        await ExecuteScalarAsync<int>(
            connectionString,
            $"SELECT count(*)::integer FROM data_collection.{table} WHERE {column} = @value",
            new NpgsqlParameter("value", value));

    private static async Task ExecuteAsync(
        string connectionString,
        string sql,
        params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, null, sql, parameters);
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql,
        params NpgsqlParameter[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
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
        return await ExecuteScalarAsync<T>(connection, null, sql, parameters);
    }

    private static async Task<T> ExecuteScalarAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql,
        params NpgsqlParameter[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        var result = await command.ExecuteScalarAsync();
        return (T)result!;
    }

    private static async Task<IReadOnlyList<string>> QueryStringsAsync(
        string connectionString,
        string sql,
        params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            for (var index = 0; index < reader.FieldCount; index++)
                values.Add(reader.GetString(index));
        }

        return values;
    }

    private static async Task<IReadOnlyList<int>> QueryIntsAsync(
        string connectionString,
        string sql,
        params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<int>();
        while (await reader.ReadAsync())
            values.Add(reader.GetInt32(0));

        return values;
    }

    private sealed record RawSeed(
        Guid SessionId,
        long RawMessageId,
        DateTimeOffset ReceivedAt,
        byte[] Payload);
}
