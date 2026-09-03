using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Normalization;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Integration.Postgres;

[Collection(PostgreSqlCollection.Name)]
public sealed class NormalizationSuitabilityReaderPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset EventEndsAt =
        DateTimeOffset.Parse("2026-09-03T12:05:00Z");
    private static readonly DateTimeOffset ResolutionSignaledAt =
        DateTimeOffset.Parse("2026-09-03T12:05:01Z");
    private static readonly DateTimeOffset ReceivedAt =
        DateTimeOffset.Parse("2026-09-03T12:04:00Z");
    private static readonly byte[] Payload = [1, 2, 3];

    [Fact]
    public async Task ReadAsync_WithAllSnapshotRowsProcessed_ShouldReturnExactCountsAndResolutionProvenance()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var sessionId = await SeedAwaitingNormalizationSessionAsync(
            database.ConnectionString);
        var rawIds = await SeedRawMessagesAsync(database.ConnectionString, sessionId, 3);
        foreach (var rawId in rawIds)
        {
            await InsertLedgerAsync(
                database.ConnectionString,
                rawId,
                3,
                NormalizationStatus.Processed);
        }

        await InsertNormalizedEventAsync(
            database.ConnectionString,
            rawIds[1],
            itemIndex: 0,
            projectionVersion: 3,
            eventType: "market_resolved",
            sessionId);
        await InsertResolutionObservationAsync(
            database.ConnectionString,
            sessionId,
            rawIds[1],
            itemIndex: 0,
            ResolutionSignaledAt,
            connectionEpoch: 1,
            winnerTokenId: "1001",
            winnerOutcome: "Yes");

        await using var context = CreateContext(database.ConnectionString);
        var reader = new NormalizationSuitabilityReader(context);
        var suitability = await reader.ReadAsync(
            CreateId(sessionId),
            3,
            CancellationToken.None);

        suitability.Should().Be(new NormalizationSuitability(
            RawCount: 3,
            LedgerCount: 3,
            ProcessedCount: 3,
            PendingCount: 0,
            ProcessingCount: 0,
            UnsupportedCount: 0,
            InvalidCount: 0,
            FailedCount: 0,
            ResolutionRawItemProcessed: true));
    }

    [Fact]
    public async Task ReadAsync_WithMixedStatusesAndMissingRow_ShouldReturnSessionScopedCounts()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var sessionId = await SeedAwaitingNormalizationSessionAsync(
            database.ConnectionString);
        var rawIds = await SeedRawMessagesAsync(database.ConnectionString, sessionId, 6);
        await InsertLedgerAsync(database.ConnectionString, rawIds[0], 3, NormalizationStatus.Processed);
        await InsertLedgerAsync(database.ConnectionString, rawIds[1], 3, NormalizationStatus.Pending);
        await InsertLedgerAsync(database.ConnectionString, rawIds[2], 3, NormalizationStatus.Processing);
        await InsertLedgerAsync(database.ConnectionString, rawIds[3], 3, NormalizationStatus.Unsupported);
        await InsertLedgerAsync(database.ConnectionString, rawIds[4], 3, NormalizationStatus.Invalid);

        var unrelatedSessionId = await SeedFailedSessionAsync(database.ConnectionString);
        var unrelatedRawIds = await SeedRawMessagesAsync(
            database.ConnectionString,
            unrelatedSessionId,
            3);
        foreach (var rawId in unrelatedRawIds)
        {
            await InsertLedgerAsync(
                database.ConnectionString,
                rawId,
                3,
                NormalizationStatus.Processed);
        }

        await using var context = CreateContext(database.ConnectionString);
        var reader = new NormalizationSuitabilityReader(context);
        var suitability = await reader.ReadAsync(
            CreateId(sessionId),
            3,
            CancellationToken.None);

        suitability.Should().Be(new NormalizationSuitability(
            RawCount: 6,
            LedgerCount: 5,
            ProcessedCount: 1,
            PendingCount: 1,
            ProcessingCount: 1,
            UnsupportedCount: 1,
            InvalidCount: 1,
            FailedCount: 0,
            ResolutionRawItemProcessed: false));
        suitability.MissingCount.Should().Be(1);
    }

    [Fact]
    public async Task ReadAsync_WithAdditionalOtherVersionRows_ShouldUseOnlyRequestedVersion()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var sessionId = await SeedAwaitingNormalizationSessionAsync(
            database.ConnectionString);
        var rawIds = await SeedRawMessagesAsync(database.ConnectionString, sessionId, 3);
        foreach (var rawId in rawIds)
        {
            await InsertLedgerAsync(
                database.ConnectionString,
                rawId,
                3,
                NormalizationStatus.Processed);
            await InsertLedgerAsync(
                database.ConnectionString,
                rawId,
                2,
                NormalizationStatus.Pending);
            await InsertLedgerAsync(
                database.ConnectionString,
                rawId,
                4,
                NormalizationStatus.Invalid);
        }

        await using var context = CreateContext(database.ConnectionString);
        var reader = new NormalizationSuitabilityReader(context);
        var suitability = await reader.ReadAsync(
            CreateId(sessionId),
            3,
            CancellationToken.None);

        suitability.Should().Be(new NormalizationSuitability(
            RawCount: 3,
            LedgerCount: 3,
            ProcessedCount: 3,
            PendingCount: 0,
            ProcessingCount: 0,
            UnsupportedCount: 0,
            InvalidCount: 0,
            FailedCount: 0,
            ResolutionRawItemProcessed: false));
    }

    [Fact]
    public async Task ReadAsync_WithProcessedEmptyRootArray_ShouldNotRequireNormalizedEvent()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var sessionId = await SeedAwaitingNormalizationSessionAsync(
            database.ConnectionString);
        var rawIds = await SeedRawMessagesAsync(database.ConnectionString, sessionId, 2);
        await InsertLedgerAsync(
            database.ConnectionString,
            rawIds[0],
            3,
            NormalizationStatus.Processed);
        await InsertLedgerAsync(
            database.ConnectionString,
            rawIds[1],
            3,
            NormalizationStatus.Processed);
        await InsertNormalizedEventAsync(
            database.ConnectionString,
            rawIds[1],
            itemIndex: 0,
            projectionVersion: 3,
            eventType: "market_resolved",
            sessionId);
        await InsertResolutionObservationAsync(
            database.ConnectionString,
            sessionId,
            rawIds[1],
            itemIndex: 0,
            ResolutionSignaledAt,
            connectionEpoch: 1,
            winnerTokenId: "1001",
            winnerOutcome: "Yes");

        await using var context = CreateContext(database.ConnectionString);
        var reader = new NormalizationSuitabilityReader(context);
        var suitability = await reader.ReadAsync(
            CreateId(sessionId),
            3,
            CancellationToken.None);

        suitability.Should().Be(new NormalizationSuitability(
            RawCount: 2,
            LedgerCount: 2,
            ProcessedCount: 2,
            PendingCount: 0,
            ProcessingCount: 0,
            UnsupportedCount: 0,
            InvalidCount: 0,
            FailedCount: 0,
            ResolutionRawItemProcessed: true));
    }

    [Theory]
    [InlineData("wrong_raw")]
    [InlineData("wrong_item")]
    [InlineData("wrong_version")]
    [InlineData("wrong_event_type")]
    [InlineData("wrong_epoch")]
    [InlineData("wrong_winner")]
    [InlineData("wrong_signal_time")]
    public async Task ReadAsync_WithMismatchedStrictResolutionProvenance_ShouldReturnFalse(
        string mismatch)
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var sessionId = await SeedAwaitingNormalizationSessionAsync(
            database.ConnectionString);
        var rawIds = await SeedRawMessagesAsync(database.ConnectionString, sessionId, 2);
        await InsertLedgerAsync(database.ConnectionString, rawIds[0], 3, NormalizationStatus.Processed);
        await InsertLedgerAsync(database.ConnectionString, rawIds[1], 3, NormalizationStatus.Processed);

        var eventType = mismatch == "wrong_event_type"
            ? "price_change"
            : "market_resolved";
        var projectionVersion = mismatch == "wrong_version" ? 2 : 3;
        await InsertNormalizedEventAsync(
            database.ConnectionString,
            rawIds[1],
            itemIndex: 0,
            projectionVersion,
            eventType,
            sessionId);

        var observationRawId = mismatch == "wrong_raw" ? rawIds[0] : rawIds[1];
        var itemIndex = mismatch == "wrong_item" ? 1 : 0;
        var observedAt = mismatch == "wrong_signal_time"
            ? ResolutionSignaledAt.AddSeconds(1)
            : ResolutionSignaledAt;
        var connectionEpoch = mismatch == "wrong_epoch" ? 2 : 1;
        var winnerTokenId = mismatch == "wrong_winner" ? "1002" : "1001";
        await InsertResolutionObservationAsync(
            database.ConnectionString,
            sessionId,
            observationRawId,
            itemIndex,
            observedAt,
            connectionEpoch,
            winnerTokenId,
            winnerOutcome: "Yes");

        await using var context = CreateContext(database.ConnectionString);
        var reader = new NormalizationSuitabilityReader(context);
        var suitability = await reader.ReadAsync(
            CreateId(sessionId),
            3,
            CancellationToken.None);

        suitability.RawCount.Should().Be(2);
        suitability.LedgerCount.Should().Be(2);
        suitability.ProcessedCount.Should().Be(2);
        suitability.PendingCount.Should().Be(0);
        suitability.ProcessingCount.Should().Be(0);
        suitability.UnsupportedCount.Should().Be(0);
        suitability.InvalidCount.Should().Be(0);
        suitability.FailedCount.Should().Be(0);
        suitability.MissingCount.Should().Be(0);
        suitability.ResolutionRawItemProcessed.Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_WhenCancelled_ShouldPropagateCancellation()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var sessionId = await SeedAwaitingNormalizationSessionAsync(
            database.ConnectionString);
        await using var context = CreateContext(database.ConnectionString);
        var reader = new NormalizationSuitabilityReader(context);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = async () => await reader.ReadAsync(
            CreateId(sessionId),
            3,
            cancellation.Token);

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

    private static CollectorSessionId CreateId(Guid sessionId) =>
        CollectorSessionId.Create(sessionId).Value;

    private static Task<Guid> SeedAwaitingNormalizationSessionAsync(
        string connectionString) =>
        SeedSessionAsync(
            connectionString,
            status: 2,
            phase: 8,
            projectionVersion: 3,
            eventEndsAt: EventEndsAt,
            resolutionSignaledAt: ResolutionSignaledAt,
            resolutionConnectionEpoch: 1,
            winningTokenId: "1001",
            winningOutcome: "Yes");

    private static Task<Guid> SeedFailedSessionAsync(string connectionString) =>
        SeedSessionAsync(
            connectionString,
            status: 4,
            phase: null,
            projectionVersion: 3,
            eventEndsAt: null,
            resolutionSignaledAt: null,
            resolutionConnectionEpoch: null,
            winningTokenId: null,
            winningOutcome: null);

    private static async Task<Guid> SeedSessionAsync(
        string connectionString,
        int status,
        int? phase,
        int? projectionVersion,
        DateTimeOffset? eventEndsAt,
        DateTimeOffset? resolutionSignaledAt,
        long? resolutionConnectionEpoch,
        string? winningTokenId,
        string? winningOutcome)
    {
        var sessionId = Guid.NewGuid();
        await ExecuteAsync(
            connectionString,
            """
            INSERT INTO data_collection.collector_sessions
                (id, market_id, status, phase, created_at, projection_version,
                 event_ends_at, resolution_signaled_at, resolution_connection_epoch,
                 winning_token_id, winning_outcome)
            VALUES (@id, @market_id, @status, @phase, @created_at, @projection_version,
                    @event_ends_at, @resolution_signaled_at, @resolution_connection_epoch,
                    @winning_token_id, @winning_outcome)
            """,
            new NpgsqlParameter("id", sessionId),
            new NpgsqlParameter("market_id", Guid.NewGuid()),
            new NpgsqlParameter("status", status),
            new NpgsqlParameter("phase", (object?)phase ?? DBNull.Value),
            new NpgsqlParameter("created_at", EventEndsAt.AddMinutes(-8)),
            new NpgsqlParameter(
                "projection_version",
                (object?)projectionVersion ?? DBNull.Value),
            new NpgsqlParameter("event_ends_at", (object?)eventEndsAt ?? DBNull.Value),
            new NpgsqlParameter(
                "resolution_signaled_at",
                (object?)resolutionSignaledAt ?? DBNull.Value),
            new NpgsqlParameter(
                "resolution_connection_epoch",
                (object?)resolutionConnectionEpoch ?? DBNull.Value),
            new NpgsqlParameter(
                "winning_token_id",
                (object?)winningTokenId ?? DBNull.Value),
            new NpgsqlParameter(
                "winning_outcome",
                (object?)winningOutcome ?? DBNull.Value));
        return sessionId;
    }

    private static async Task<IReadOnlyList<long>> SeedRawMessagesAsync(
        string connectionString,
        Guid sessionId,
        int count)
    {
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
                new NpgsqlParameter("received_at", ReceivedAt.AddSeconds(index)),
                new NpgsqlParameter("payload", Payload)));
        }

        return ids;
    }

    private static Task InsertLedgerAsync(
        string connectionString,
        long rawMessageId,
        int projectionVersion,
        NormalizationStatus status) =>
        ExecuteAsync(
            connectionString,
            """
            INSERT INTO data_collection.raw_message_normalizations
                (raw_message_id, projection_version, status, attempt_count)
            VALUES (@raw_message_id, @projection_version, @status, 0)
            """,
            new NpgsqlParameter("raw_message_id", rawMessageId),
            new NpgsqlParameter("projection_version", projectionVersion),
            new NpgsqlParameter("status", (int)status));

    private static Task InsertNormalizedEventAsync(
        string connectionString,
        long rawMessageId,
        int itemIndex,
        int projectionVersion,
        string eventType,
        Guid sessionId) =>
        ExecuteAsync(
            connectionString,
            """
            INSERT INTO data_collection.normalized_events
                (raw_message_id, raw_item_index, projection_version,
                 normalizer_version, event_type, session_id, received_at, normalized_at)
            VALUES (@raw_message_id, @raw_item_index, @projection_version,
                    1, @event_type, @session_id, @received_at, @normalized_at)
            """,
            new NpgsqlParameter("raw_message_id", rawMessageId),
            new NpgsqlParameter("raw_item_index", itemIndex),
            new NpgsqlParameter("projection_version", projectionVersion),
            new NpgsqlParameter("event_type", eventType),
            new NpgsqlParameter("session_id", sessionId),
            new NpgsqlParameter("received_at", ReceivedAt),
            new NpgsqlParameter("normalized_at", ReceivedAt.AddSeconds(1)));

    private static Task InsertResolutionObservationAsync(
        string connectionString,
        Guid sessionId,
        long rawMessageId,
        int itemIndex,
        DateTimeOffset observedAt,
        long connectionEpoch,
        string winnerTokenId,
        string winnerOutcome) =>
        ExecuteAsync(
            connectionString,
            """
            INSERT INTO data_collection.resolution_observations
                (session_id, source, observed_at, status, winner_token_id,
                 winner_outcome, raw_message_id, raw_item_index, connection_epoch)
            VALUES (@session_id, 0, @observed_at, 2, @winner_token_id,
                    @winner_outcome, @raw_message_id, @raw_item_index, @connection_epoch)
            """,
            new NpgsqlParameter("session_id", sessionId),
            new NpgsqlParameter("observed_at", observedAt),
            new NpgsqlParameter("winner_token_id", winnerTokenId),
            new NpgsqlParameter("winner_outcome", winnerOutcome),
            new NpgsqlParameter("raw_message_id", rawMessageId),
            new NpgsqlParameter("raw_item_index", itemIndex),
            new NpgsqlParameter("connection_epoch", connectionEpoch));

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
