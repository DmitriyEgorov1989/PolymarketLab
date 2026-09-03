using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.CollectorSession;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.RawMarketMessage;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using System.Text;
using System.Data.Common;
using Xunit;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Integration.Postgres;

[Collection(PostgreSqlCollection.Name)]
public sealed class RawMarketMessageWriterPostgreSqlTests(PostgreSqlFixture fixture)
{
    private const string PreviousMigration =
        "20260828110547_PersistCollectorSessionSnapshotAndGlobalExclusivity";
    private const string AccountingMigration =
        "20260831121534_PersistConnectionEpochAndExactRawAccounting";
    private static readonly DateTimeOffset CreatedAt =
        DateTimeOffset.Parse("2026-08-31T11:00:00Z");

    [Theory]
    [InlineData(CollectorSessionStatus.Invalidating)]
    [InlineData(CollectorSessionStatus.Failed)]
    public async Task WriteBatchAsync_AfterInvalidationFence_ShouldNotPersistRawOrProgress(
        CollectorSessionStatus status)
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var session = await InsertSessionAsync(database.ConnectionString);
        await using (var fenceContext = CreateContext(database.ConnectionString))
        {
            await fenceContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE data_collection.collector_sessions SET status = {(int)status}, invalidating_at = CURRENT_TIMESTAMP WHERE id = {session.Id.Value}");
        }
        await using var context = CreateContext(database.ConnectionString);

        var result = await new RawMarketMessageWriter(context).WriteBatchAsync(
            CreateMessages(session.Id, 1, 2, "fenced"),
            [CreateCheckpoint(session.Id, 1, 2)],
            CancellationToken.None);

        result.FencedSessionIds.Should().Equal(session.Id);
        result.PersistedSessionIds.Should().BeEmpty();
        await using var verificationContext = CreateContext(database.ConnectionString);
        var progress = await new CollectorSessionProgressRepository(verificationContext)
            .GetAsync(session.Id, CancellationToken.None);
        progress.MessagesPersisted.Should().Be(0);
        progress.RawMessageCount.Should().Be(0);
    }

    [Fact]
    public async Task InvalidationFence_WhenRawWriteIsInFlight_ShouldWaitAndRejectLaterWrites()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var session = await InsertSessionAsync(database.ConnectionString);
        var writeBarrier = new SessionFenceLockBarrierInterceptor();
        var invalidationStarted = new CommandStartedInterceptor(
            "UPDATE data_collection.collector_sessions");

        var inFlightWrite = WriteAsync(
            database.ConnectionString,
            CreateMessages(session.Id, 1, 1, "before-fence"),
            CreateCheckpoint(session.Id, 1, 1),
            writeBarrier);
        await writeBarrier.LockAcquired;

        await using var invalidationContext = CreateContext(
            database.ConnectionString,
            invalidationStarted);
        var invalidation = invalidationContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE data_collection.collector_sessions SET status = {(int)CollectorSessionStatus.Invalidating}, invalidating_at = CURRENT_TIMESTAMP WHERE id = {session.Id.Value}");
        await invalidationStarted.Started;
        invalidation.IsCompleted.Should().BeFalse();

        writeBarrier.Release();
        await inFlightWrite;
        await invalidation;

        await using var staleWriterContext = CreateContext(database.ConnectionString);
        var staleWrite = await new RawMarketMessageWriter(staleWriterContext).WriteBatchAsync(
            CreateMessages(session.Id, 2, 1, "after-fence"),
            [CreateCheckpoint(session.Id, 2, 2)],
            CancellationToken.None);

        staleWrite.FencedSessionIds.Should().Equal(session.Id);
        await using var verificationContext = CreateContext(database.ConnectionString);
        var progress = await new CollectorSessionProgressRepository(verificationContext)
            .GetAsync(session.Id, CancellationToken.None);
        progress.MessagesPersisted.Should().Be(1);
        progress.RawMessageCount.Should().Be(1);
    }

    [Fact]
    public async Task WriteAndCheckpoint_ShouldRoundTripExactDurableAccountingAfterRestart()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var session = await InsertSessionAsync(database.ConnectionString);
        var firstPayload = "first"u8.ToArray();
        var messages = new RawMarketMessage[]
        {
            new(session.Id, 1, CreatedAt.AddSeconds(1), firstPayload),
            new(session.Id, 2, CreatedAt.AddSeconds(2), "second"u8.ToArray())
        };
        var checkpoint = new CollectorSessionProgressCheckpoint(
            session.Id,
            2,
            3,
            2,
            0,
            CreatedAt.AddSeconds(3),
            1);

        await WriteAsync(database.ConnectionString, messages, checkpoint);
        firstPayload[0] = (byte)'X';
        await CheckpointAsync(
            database.ConnectionString,
            checkpoint with
            {
                CurrentConnectionEpoch = 3,
                MessagesReceived = 4,
                MessagesEnqueued = 3,
                MessagesPersisted = 2,
                ReconnectCount = 2
            });
        await CheckpointAsync(database.ConnectionString, checkpoint);
        await CheckpointAsync(database.ConnectionString, checkpoint);

        await using var restartContext = CreateContext(database.ConnectionString);
        var progress = await new CollectorSessionProgressRepository(restartContext)
            .GetAsync(session.Id, CancellationToken.None);
        var raw = await restartContext.RawMarketMessages
            .OrderBy(message => message.Id)
            .ToArrayAsync();

        progress.CurrentConnectionEpoch.Should().Be(3);
        progress.MessagesReceived.Should().Be(4);
        progress.MessagesEnqueued.Should().Be(3);
        progress.MessagesPersisted.Should().Be(2);
        progress.RawMessageCount.Should().Be(2);
        progress.LastMessageAt.Should().Be(CreatedAt.AddSeconds(3));
        progress.ReconnectCount.Should().Be(2);
        raw.Select(message => message.ConnectionEpoch).Should().Equal(1, 2);
        raw[0].Payload.Should().Equal("first"u8.ToArray());
        raw[1].Payload.Should().Equal("second"u8.ToArray());
    }

    [Fact]
    public async Task ConcurrentWriters_ShouldNotLoseRawRowsOrPersistedIncrements()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var session = await InsertSessionAsync(database.ConnectionString);
        var first = CreateMessages(session.Id, 1, 2, "first");
        var second = CreateMessages(session.Id, 2, 3, "second");
        var upsertBarrier = new ProgressUpsertBarrierInterceptor(2);

        await Task.WhenAll(
            WriteAsync(
                database.ConnectionString,
                first,
                CreateCheckpoint(session.Id, 1, 5),
                upsertBarrier),
            WriteAsync(
                database.ConnectionString,
                second,
                CreateCheckpoint(session.Id, 2, 5),
                upsertBarrier));

        await using var context = CreateContext(database.ConnectionString);
        var progress = await new CollectorSessionProgressRepository(context)
            .GetAsync(session.Id, CancellationToken.None);

        progress.MessagesReceived.Should().Be(5);
        progress.MessagesEnqueued.Should().Be(5);
        progress.MessagesPersisted.Should().Be(5);
        progress.RawMessageCount.Should().Be(5);
        progress.CurrentConnectionEpoch.Should().Be(2);
    }

    [Fact]
    public async Task WriteBatch_ShouldNotSynthesizeReceivedOrEnqueuedObservations()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var session = await InsertSessionAsync(database.ConnectionString);

        await WriteAsync(
            database.ConnectionString,
            CreateMessages(session.Id, 1, 2, "observation"),
            new CollectorSessionProgressCheckpoint(
                session.Id,
                1,
                2,
                1,
                0,
                CreatedAt,
                0));

        await using var context = CreateContext(database.ConnectionString);
        var progress = await new CollectorSessionProgressRepository(context)
            .GetAsync(session.Id, CancellationToken.None);
        progress.MessagesReceived.Should().Be(2);
        progress.MessagesEnqueued.Should().Be(1);
        progress.MessagesPersisted.Should().Be(2);
        progress.RawMessageCount.Should().Be(2);
    }

    [Fact]
    public async Task EmptyCheckpoint_ShouldPersistZeroObservationsAndNullTimestamp()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var session = await InsertSessionAsync(database.ConnectionString);
        await using var context = CreateContext(database.ConnectionString);
        var repository = new CollectorSessionProgressRepository(context);

        await repository.CheckpointAsync(
            new CollectorSessionProgressCheckpoint(
                session.Id,
                0,
                0,
                0,
                0,
                null,
                0),
            CancellationToken.None);

        var progress = await repository.GetAsync(session.Id, CancellationToken.None);
        progress.Should().Be(CollectorSessionProgress.Empty(session.Id));
    }

    [Fact]
    public async Task WriteBatchAsync_WhenProgressUpsertFails_ShouldRollBackRawRows()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var session = await InsertSessionAsync(database.ConnectionString);
        await using (var setup = CreateContext(database.ConnectionString))
        {
            await setup.Database.ExecuteSqlRawAsync(
                """
                CREATE FUNCTION data_collection.reject_progress_update()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Rejected progress update.';
                END;
                $$;
                CREATE TRIGGER reject_progress_update
                BEFORE UPDATE ON data_collection.collector_session_progress
                FOR EACH ROW EXECUTE FUNCTION data_collection.reject_progress_update();
                """);
        }

        Func<Task> write = () => WriteAsync(
            database.ConnectionString,
            CreateMessages(session.Id, 1, 1, "rollback"),
            CreateCheckpoint(session.Id, 1, 1));

        await write.Should().ThrowAsync<PostgresException>();
        await using var context = CreateContext(database.ConnectionString);
        var progress = await new CollectorSessionProgressRepository(context)
            .GetAsync(session.Id, CancellationToken.None);
        progress.MessagesPersisted.Should().Be(0);
        progress.RawMessageCount.Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_WithRestartContext_ShouldReadExactCountersInSingleCommand()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var session = await InsertSessionAsync(database.ConnectionString);
        await WriteAsync(
            database.ConnectionString,
            CreateMessages(session.Id, 1, 3, "restart"),
            new CollectorSessionProgressCheckpoint(
                session.Id,
                1,
                3,
                3,
                3,
                CreatedAt,
                0));

        var readerCounter = new ReaderCountInterceptor();
        await using var restartContext = CreateContext(
            database.ConnectionString,
            readerCounter);
        var progress = await new CollectorSessionProgressRepository(restartContext)
            .GetAsync(session.Id, CancellationToken.None);

        progress.MessagesReceived.Should().Be(3);
        progress.MessagesEnqueued.Should().Be(3);
        progress.MessagesPersisted.Should().Be(3);
        progress.RawMessageCount.Should().Be(3);
        readerCounter.ExecutedReaderCount.Should().Be(1);
    }

    [Fact]
    public async Task CheckpointAsync_WhenRepeatedWithIdenticalFinalCheckpoint_ShouldNotDoubleCounters()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var session = await InsertSessionAsync(database.ConnectionString);
        var finalCheckpoint = new CollectorSessionProgressCheckpoint(
            session.Id,
            2,
            3,
            3,
            3,
            CreatedAt,
            0);

        await CheckpointAsync(database.ConnectionString, finalCheckpoint);
        await CheckpointAsync(database.ConnectionString, finalCheckpoint);

        await using var context = CreateContext(database.ConnectionString);
        var progress = await new CollectorSessionProgressRepository(context)
            .GetAsync(session.Id, CancellationToken.None);
        progress.MessagesReceived.Should().Be(3);
        progress.MessagesEnqueued.Should().Be(3);
        progress.MessagesPersisted.Should().Be(3);
        progress.CurrentConnectionEpoch.Should().Be(2);
    }

    [Fact]
    public async Task GetAsync_WhenRawCountDiffersFromCheckpoint_ShouldReturnDistinctValues()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var session = await InsertSessionAsync(database.ConnectionString);
        await using (var setup = CreateContext(database.ConnectionString))
        {
            await setup.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO data_collection.raw_market_messages (
                    session_id,
                    connection_epoch,
                    received_at,
                    payload)
                SELECT
                    {session.Id.Value},
                    1,
                    {CreatedAt},
                    'mismatch'::bytea
                FROM generate_series(1, 1249);
                """);
        }
        await CheckpointAsync(
            database.ConnectionString,
            new CollectorSessionProgressCheckpoint(
                session.Id,
                1,
                1250,
                1250,
                1250,
                CreatedAt,
                0));

        await using var context = CreateContext(database.ConnectionString);
        var progress = await new CollectorSessionProgressRepository(context)
            .GetAsync(session.Id, CancellationToken.None);

        progress.MessagesReceived.Should().Be(1250);
        progress.MessagesEnqueued.Should().Be(1250);
        progress.MessagesPersisted.Should().Be(1250);
        progress.RawMessageCount.Should().Be(1249);
    }

    [Fact]
    public async Task WriteBatchAsync_WhenCancelled_ShouldNotPersistRawOrProgress()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var session = await InsertSessionAsync(database.ConnectionString);
        await using var context = CreateContext(database.ConnectionString);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        Func<Task> write = () => new RawMarketMessageWriter(context).WriteBatchAsync(
            CreateMessages(session.Id, 1, 1, "cancelled"),
            [CreateCheckpoint(session.Id, 1, 1)],
            cancellationSource.Token);

        await write.Should().ThrowAsync<OperationCanceledException>();
        await using var verificationContext = CreateContext(database.ConnectionString);
        var progress = await new CollectorSessionProgressRepository(verificationContext)
            .GetAsync(session.Id, CancellationToken.None);
        progress.MessagesPersisted.Should().Be(0);
        progress.RawMessageCount.Should().Be(0);
    }

    [Fact]
    public async Task Migration_WhenRawArchiveIsNotEmpty_ShouldFailWithoutChangingSchema()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var session = await InsertSessionAsync(database.ConnectionString);
        await WriteAsync(
            database.ConnectionString,
            CreateMessages(session.Id, 1, 1, "historical"),
            CreateCheckpoint(session.Id, 1, 1));
        await using (var downgradeContext = CreateContext(database.ConnectionString))
        {
            await downgradeContext.Database.GetService<IMigrator>()
                .MigrateAsync(PreviousMigration);
        }

        await using var upgradeContext = CreateContext(database.ConnectionString);
        Func<Task> migrate = () => upgradeContext.Database.MigrateAsync();

        await migrate.Should().ThrowAsync<PostgresException>()
            .WithMessage("*raw_market_messages is not empty*");
        var applied = await upgradeContext.Database.GetAppliedMigrationsAsync();
        applied.Should().NotContain(AccountingMigration);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = 'data_collection'
              AND table_name = 'raw_market_messages'
              AND column_name = 'connection_epoch';
            """;
        Convert.ToInt64(await command.ExecuteScalarAsync()).Should().Be(0);
    }

    private async Task<PostgreSqlTestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await fixture.CreateDatabaseAsync();
        await using var context = CreateContext(database.ConnectionString);
        await context.Database.MigrateAsync();
        return database;
    }

    private static async Task<CollectorSessionAggregate> InsertSessionAsync(
        string connectionString)
    {
        var session = CreateSession();
        await using var context = CreateContext(connectionString);
        var result = await new CollectorSessionRepository(context)
            .TryAddAsync(session, CancellationToken.None);
        result.Value.Should().Be(CollectorSessionInsertStatus.Inserted);
        return session;
    }

    private static async Task WriteAsync(
        string connectionString,
        IReadOnlyCollection<RawMarketMessage> messages,
        CollectorSessionProgressCheckpoint checkpoint,
        DbCommandInterceptor? interceptor = null)
    {
        await using var context = CreateContext(connectionString, interceptor);
        await new RawMarketMessageWriter(context).WriteBatchAsync(
            messages,
            [checkpoint],
            CancellationToken.None);
    }

    private static async Task CheckpointAsync(
        string connectionString,
        CollectorSessionProgressCheckpoint checkpoint)
    {
        await using var context = CreateContext(connectionString);
        await new CollectorSessionProgressRepository(context)
            .CheckpointAsync(checkpoint, CancellationToken.None);
    }

    private static RawMarketMessage[] CreateMessages(
        CollectorSessionId sessionId,
        long connectionEpoch,
        int count,
        string prefix) => Enumerable.Range(0, count)
            .Select(index => new RawMarketMessage(
                sessionId,
                connectionEpoch,
                CreatedAt.AddMilliseconds(index),
                Encoding.UTF8.GetBytes($"{prefix}-{index}")))
            .ToArray();

    private static CollectorSessionProgressCheckpoint CreateCheckpoint(
        CollectorSessionId sessionId,
        long connectionEpoch,
        long messageCount) => new(
            sessionId,
            connectionEpoch,
            messageCount,
            messageCount,
            0,
            CreatedAt.AddSeconds(messageCount),
            connectionEpoch - 1);

    private static DataCollectionDbContext CreateContext(
        string connectionString,
        DbCommandInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<DataCollectionDbContext>()
            .UseNpgsql(connectionString);
        if (interceptor is not null)
            builder.AddInterceptors(interceptor);

        return new DataCollectionDbContext(builder.Options);
    }

    private static CollectorSessionAggregate CreateSession() =>
        CollectorSessionAggregate.Create(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            MarketId.Create(Guid.NewGuid()).Value,
            "event-raw-accounting",
            "raw-accounting-event",
            "market-raw-accounting",
            "raw-accounting-market",
            "0xrawaccounting",
            CreatedAt.AddMinutes(3),
            CreatedAt.AddMinutes(8),
            3,
            [
                new CollectorSessionTokenDefinition(
                    TokenId.Create("1001").Value,
                    "Yes",
                    0),
                new CollectorSessionTokenDefinition(
                    TokenId.Create("1002").Value,
                    "No",
                    1)
            ],
            CreatedAt).Value;

    private sealed class ProgressUpsertBarrierInterceptor(int participantCount)
        : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _allArrived = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.Contains(
                    "INSERT INTO data_collection.collector_session_progress",
                    StringComparison.Ordinal))
            {
                return result;
            }

            if (Interlocked.Increment(ref _arrived) == participantCount)
                _allArrived.TrySetResult();

            await _allArrived.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    private sealed class SessionFenceLockBarrierInterceptor : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _lockAcquired = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task LockAcquired => _lockAcquired.Task;

        public void Release() => _release.TrySetResult();

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(
                    "FROM data_collection.collector_sessions",
                    StringComparison.Ordinal)
                && command.CommandText.Contains("FOR SHARE", StringComparison.Ordinal))
            {
                _lockAcquired.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }

    private sealed class CommandStartedInterceptor(string commandFragment)
        : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(commandFragment, StringComparison.Ordinal))
                _started.TrySetResult();

            return ValueTask.FromResult(result);
        }
    }

    private sealed class ReaderCountInterceptor : DbCommandInterceptor
    {
        private int _executedReaderCount;

        public int ExecutedReaderCount => Volatile.Read(ref _executedReaderCount);

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _executedReaderCount);
            return ValueTask.FromResult(result);
        }
    }
}
