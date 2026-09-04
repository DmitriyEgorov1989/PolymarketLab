using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PolymarketLab.DataCollection.Core.Application.UseCases.Common;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.CollectorSession;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Normalization;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Integration.Postgres;

[Collection(PostgreSqlCollection.Name)]
public sealed class CollectorSessionReadModelPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset CreatedAt =
        DateTimeOffset.Parse("2026-09-04T11:57:00Z");
    private static readonly DateTimeOffset InvalidatingAt =
        DateTimeOffset.Parse("2026-09-04T12:06:00Z");
    private static readonly DateTimeOffset CompletedAt =
        DateTimeOffset.Parse("2026-09-04T12:07:00Z");

    [Fact]
    public async Task CleanedFailedSession_ShouldMapFullReadModelThroughSamePorts()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var unrelated = await InsertSessionAsync(database.ConnectionString, terminal: true);
        await InsertRawMessageAsync(database.ConnectionString, unrelated.Id);

        var target = await InsertSessionAsync(database.ConnectionString, terminal: false);
        await InsertRawMessageAsync(database.ConnectionString, target.Id);
        await using (var context = CreateContext(database.ConnectionString))
        {
            await new CollectorSessionProgressRepository(context).CheckpointAsync(
                new CollectorSessionProgressCheckpoint(
                    target.Id,
                    1,
                    5,
                    5,
                    5,
                    InvalidatingAt.AddSeconds(-1),
                    1),
                CancellationToken.None);
            await new CollectorTokenReadinessRepository(context)
                .RecordInitialBookEnqueuedAsync(
                    new CollectorTokenReadiness(
                        target.Id,
                        1,
                        TokenId.Create("1001").Value,
                        InvalidatingAt.AddSeconds(-2)),
                    CancellationToken.None);
            await new ResolutionObservationRepository(context).SaveFailureAsync(
                new DurableResolutionFailure(
                    target.Id,
                    ResolutionObservationSource.Gamma,
                    InvalidatingAt.AddSeconds(-1),
                    "collector.resolution.gamma.error",
                    "Gamma check failed."),
                CancellationToken.None);
        }

        target.BeginInvalidation(
                InvalidatingAt,
                CollectorStopReason.PersistenceFailure,
                "collector.runtime.persist.failed",
                "Persistence failed.")
            .IsSuccess.Should().BeTrue();
        await using (var context = CreateContext(database.ConnectionString))
        {
            (await new CollectorSessionRepository(context).TryUpdateAsync(
                target,
                CollectorSessionStatus.Scheduled,
                CancellationToken.None)).Value.Should().Be(
                CollectorSessionUpdateStatus.Updated);
        }

        await using (var context = CreateContext(database.ConnectionString))
        {
            var cleanup = new CollectorDatasetCleanup(
                context,
                new FixedTimeProvider(CompletedAt));
            var result = await cleanup.CleanupAsync(target, CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
        }

        await using (var context = CreateContext(database.ConnectionString))
        {
            var sessionRepository = new CollectorSessionRepository(context);
            var progressRepository = new CollectorSessionProgressRepository(context);
            var tokenReadinessRepository = new CollectorTokenReadinessRepository(context);
            var resolutionRepository = new ResolutionObservationRepository(context);
            var cleanupAuditReader = new CollectorDatasetCleanupAuditReader(context);
            var normalizationReader = new NormalizationSuitabilityReader(context);
            var factory = new CollectorSessionResponseFactory(
                progressRepository,
                tokenReadinessRepository,
                resolutionRepository,
                cleanupAuditReader,
                normalizationReader);
            var persisted = await sessionRepository.GetByIdAsync(
                target.Id,
                CancellationToken.None);
            persisted.Should().NotBeNull();
            persisted!.Status.Should().Be(CollectorSessionStatus.Failed);

            var response = await factory.CreateAsync(persisted, CancellationToken.None);

            response.Status.Should().Be("Failed");
            response.Phase.Should().BeNull();
            response.MessagesReceived.Should().Be(5);
            response.MessagesEnqueued.Should().Be(5);
            response.MessagesPersisted.Should().Be(5);
            response.RemainingRawMessageCount.Should().Be(0);
            response.Normalization.Should().BeNull();
            response.Cleanup.Should().NotBeNull();
            response.Cleanup!.CleanedAt.Should().Be(CompletedAt);
            response.Cleanup.InvalidatingAt.Should().Be(InvalidatingAt);
            response.Cleanup.ProjectionVersion.Should().Be(2);
            response.Cleanup.FailureCode.Should().Be("collector.runtime.persist.failed");
            response.Cleanup.FailureMessage.Should().Be("Persistence failed.");
            response.Cleanup.DeletedRawMessageCount.Should().Be(1);
            response.Cleanup.DeletedNormalizationCount.Should().Be(2);
            response.Cleanup.DeletedNormalizedEventCount.Should().Be(0);
            response.Readiness.ConnectionEpoch.Should().Be(1);
            response.Readiness.Tokens.Should().Equal(
                new CollectorTokenReadinessResponse(
                    "1001",
                    InvalidatingAt.AddSeconds(-2)),
                new CollectorTokenReadinessResponse("1002", null));
            response.Resolution.SourceStates.Should().ContainSingle().Which.Should()
                .BeEquivalentTo(new CollectorResolutionSourceResponse(
                    "Gamma",
                    "Failed",
                    InvalidatingAt.AddSeconds(-1),
                    null,
                    null,
                    "collector.resolution.gamma.error",
                    "Gamma check failed."));
            response.Resolution.ConfirmationSources.Should().BeEmpty();

            var retainedReadiness = await tokenReadinessRepository.GetAsync(
                target.Id,
                1,
                CancellationToken.None);
            retainedReadiness.Should().ContainSingle().Which.TokenId.Value
                .Should().Be("1001");
            var retainedResolution = await resolutionRepository.GetStateAsync(
                target.Id,
                CancellationToken.None);
            retainedResolution.Observations.Should().ContainSingle();
        }

        (await ReadCountAsync(
            database.ConnectionString,
            "raw_market_messages",
            unrelated.Id)).Should().Be(1);
    }

    [Fact]
    public async Task LegacyInterruptedSession_ShouldExposeNullableReadModel()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var sessionId = CollectorSessionId.Create(Guid.NewGuid()).Value;
        var marketId = MarketId.Create(Guid.NewGuid()).Value;
        await InsertLegacySessionRowAsync(
            database.ConnectionString,
            sessionId,
            marketId);

        await using var context = CreateContext(database.ConnectionString);
        var session = await new CollectorSessionRepository(context)
            .GetByIdAsync(sessionId, CancellationToken.None);
        session.Should().NotBeNull();
        var factory = new CollectorSessionResponseFactory(
            new CollectorSessionProgressRepository(context),
            new CollectorTokenReadinessRepository(context),
            new ResolutionObservationRepository(context),
            new CollectorDatasetCleanupAuditReader(context),
            new NormalizationSuitabilityReader(context));

        var response = await factory.CreateAsync(session!, CancellationToken.None);

        response.Status.Should().Be("Interrupted");
        response.Phase.Should().BeNull();
        response.EffectiveDeadline.Should().BeNull();
        response.Snapshot.ExternalEventId.Should().BeNull();
        response.Snapshot.EventSlug.Should().BeNull();
        response.Snapshot.ExternalMarketId.Should().BeNull();
        response.Snapshot.MarketSlug.Should().BeNull();
        response.Snapshot.ConditionId.Should().BeNull();
        response.Snapshot.EventStartsAt.Should().BeNull();
        response.Snapshot.EventEndsAt.Should().BeNull();
        response.Snapshot.ProjectionVersion.Should().BeNull();
        response.Snapshot.Tokens.Should().BeEmpty();
        response.StopReason.Should().Be("ProcessTerminated");
        response.Normalization.Should().BeNull();
        response.Cleanup.Should().BeNull();
        response.Readiness.ConnectionEpoch.Should().Be(0);
        response.Readiness.Tokens.Should().BeEmpty();
        response.Resolution.SourceStates.Should().BeEmpty();
        response.Resolution.ConfirmationSources.Should().BeEmpty();
    }

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
        (await new CollectorSessionRepository(context).TryAddAsync(
            session,
            CancellationToken.None)).Value.Should().Be(
            CollectorSessionInsertStatus.Inserted);

        if (terminal)
        {
            session.Stop(CreatedAt.AddSeconds(20), CollectorStopReason.MarketClosed)
                .IsSuccess.Should().BeTrue();
            (await new CollectorSessionRepository(context).TryUpdateAsync(
                session,
                CollectorSessionStatus.Scheduled,
                CancellationToken.None)).Value.Should().Be(
                CollectorSessionUpdateStatus.Updated);
        }

        return session;
    }

    private static async Task InsertRawMessageAsync(
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
            )
            INSERT INTO data_collection.raw_message_normalizations
                (raw_message_id, projection_version, status, attempt_count)
            SELECT id, version, 2, 1
            FROM raw CROSS JOIN (VALUES (1), (2)) AS versions(version);
            """;
        command.Parameters.AddWithValue("session_id", sessionId.Value);
        command.Parameters.AddWithValue("received_at", CreatedAt);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertLegacySessionRowAsync(
        string connectionString,
        CollectorSessionId sessionId,
        MarketId marketId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            -- Явная legacy строка с NULL snapshot-полями:
            -- status 5 = Interrupted, stop_reason 8 = ProcessTerminated (числовые enum в БД).
            INSERT INTO data_collection.collector_sessions (
                id, market_id, external_event_id, event_slug, external_market_id,
                market_slug, condition_id, event_starts_at, event_ends_at,
                projection_version, status, phase, created_at, started_at,
                subscription_ready_at, resolution_signaled_at,
                resolution_confirmed_at, awaiting_normalization_at, winning_token_id,
                winning_outcome, resolution_connection_epoch, stopped_at,
                invalidating_at, stop_reason, failure_code, failure_message,
                exclusive_slot)
            VALUES (
                @session_id, @market_id, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
                NULL, 5, NULL, @created_at, @started_at, NULL, NULL, NULL, NULL,
                NULL, NULL, NULL, @stopped_at, NULL, 8, NULL, NULL, 1);
            """;
        command.Parameters.AddWithValue("session_id", sessionId.Value);
        command.Parameters.AddWithValue("market_id", marketId.Value);
        command.Parameters.AddWithValue("created_at", CreatedAt);
        command.Parameters.AddWithValue("started_at", CreatedAt.AddMinutes(1));
        command.Parameters.AddWithValue("stopped_at", CreatedAt.AddMinutes(10));
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
        command.CommandText =
            $"SELECT COUNT(*) FROM data_collection.{table} WHERE session_id = @session_id";
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
