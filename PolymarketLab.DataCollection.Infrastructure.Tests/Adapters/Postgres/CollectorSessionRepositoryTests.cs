using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.CollectorSession;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.Postgres;

public sealed class CollectorSessionRepositoryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TryAddAsync_ShouldCreateDurableProgressRow()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateOptions(databaseRoot);
        var session = CreateSession();
        await using (var context = new DataCollectionDbContext(options))
        {
            var repository = new CollectorSessionRepository(context);
            await repository.TryAddAsync(session, CancellationToken.None);
            var progressRepository = new CollectorSessionProgressRepository(context);
            await progressRepository.CheckpointAsync(
                new CollectorSessionProgressCheckpoint(
                    session.Id,
                    4,
                    Now.AddSeconds(1),
                    2),
                CancellationToken.None);
        }

        await using var verificationContext = new DataCollectionDbContext(options);
        var progress = await new CollectorSessionProgressRepository(verificationContext)
            .GetAsync(session.Id, CancellationToken.None);

        progress.MessagesReceived.Should().Be(4);
        progress.MessagesPersisted.Should().Be(0);
        progress.LastMessageAt.Should().Be(Now.AddSeconds(1));
        progress.ReconnectCount.Should().Be(2);
    }

    [Fact]
    public async Task TryAddAsync_ShouldRoundTripImmutableSnapshotAndOrderedTokens()
    {
        var options = CreateOptions(new InMemoryDatabaseRoot());
        var session = CreateSession();
        await using (var writeContext = new DataCollectionDbContext(options))
        {
            var insert = await new CollectorSessionRepository(writeContext)
                .TryAddAsync(session, CancellationToken.None);
            insert.Value.Should().Be(CollectorSessionInsertStatus.Inserted);
        }

        await using var readContext = new DataCollectionDbContext(options);
        var persisted = await new CollectorSessionRepository(readContext)
            .GetByIdAsync(session.Id, CancellationToken.None);

        persisted.Should().NotBeNull();
        persisted!.ExternalEventId.Should().Be("event-123");
        persisted.ConditionId.Should().Be("0xabc");
        persisted.EventStartsAt.Should().Be(Now.AddMinutes(3));
        persisted.EventEndsAt.Should().Be(Now.AddMinutes(8));
        persisted.ProjectionVersion.Should().Be(3);
        persisted.Tokens.Select(token => (token.TokenId.Value, token.OutcomeIndex))
            .Should()
            .Equal(("1001", 0), ("1002", 1));
    }

    [Fact]
    public async Task TryUpdateAsync_WhenExpectedStatusChanged_ShouldReturnConflict()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateOptions(databaseRoot);
        var session = CreateSession();
        await using (var seedContext = new DataCollectionDbContext(options))
        {
            var seedRepository = new CollectorSessionRepository(seedContext);
            await seedRepository.TryAddAsync(session, CancellationToken.None);
        }

        await using var runningContext = new DataCollectionDbContext(options);
        await using var failedContext = new DataCollectionDbContext(options);
        var runningRepository = new CollectorSessionRepository(runningContext);
        var failedRepository = new CollectorSessionRepository(failedContext);
        var runningSession = await runningRepository.GetByIdAsync(
            session.Id,
            CancellationToken.None);
        var failedSession = await failedRepository.GetByIdAsync(
            session.Id,
            CancellationToken.None);
        MarkRunning(runningSession!, Now.AddSeconds(1));
        failedSession!.Fail(
            Now.AddSeconds(2),
            CollectorStopReason.FatalWebSocketError,
            "collector.runtime.receive.failed",
            "Receive failed.");

        var failedUpdate = await failedRepository.TryUpdateAsync(
            failedSession,
            CollectorSessionStatus.Scheduled,
            CancellationToken.None);
        var staleRunningUpdate = await runningRepository.TryUpdateAsync(
            runningSession!,
            CollectorSessionStatus.Scheduled,
            CancellationToken.None);

        failedUpdate.Value.Should().Be(CollectorSessionUpdateStatus.Updated);
        staleRunningUpdate.Value.Should().Be(
            CollectorSessionUpdateStatus.ConcurrencyConflict);

        await using var verificationContext = new DataCollectionDbContext(options);
        var persisted = await new CollectorSessionRepository(verificationContext)
            .GetByIdAsync(session.Id, CancellationToken.None);
        persisted!.Status.Should().Be(CollectorSessionStatus.Failed);
    }

    [Fact]
    public async Task GetActiveAsync_ShouldReturnAllExclusiveStatuses()
    {
        var options = CreateOptions(new InMemoryDatabaseRoot());
        var scheduled = CreateSession();
        var starting = CreateSession();
        starting.BeginPreparation(Now);
        var running = CreateSession();
        MarkRunning(running, Now.AddSeconds(1));
        var stopping = CreateSession();
        MarkRunning(stopping, Now.AddSeconds(1));
        stopping.MarkStopping();
        var invalidating = CreateSession();
        invalidating.BeginInvalidation();
        var stopped = CreateSession();
        stopped.Stop(Now.AddSeconds(1), CollectorStopReason.Requested);
        var failed = CreateSession();
        failed.Fail(
            Now.AddSeconds(1),
            CollectorStopReason.StartupFailure,
            "collector.start.failed",
            "Start failed.");
        var interrupted = CreateSession();
        interrupted.Interrupt(
            Now.AddSeconds(1),
            CollectorStopReason.ProcessTerminated);
        await using var context = new DataCollectionDbContext(options);
        context.CollectorSessions.AddRange(
            scheduled,
            starting,
            running,
            stopping,
            invalidating,
            stopped,
            failed,
            interrupted);
        await context.SaveChangesAsync();
        var repository = new CollectorSessionRepository(context);

        var activeSessions = await repository.GetActiveAsync(
            CancellationToken.None);

        activeSessions.Select(session => session.Status).Should().BeEquivalentTo(
            [
                CollectorSessionStatus.Scheduled,
                CollectorSessionStatus.Starting,
                CollectorSessionStatus.Running,
                CollectorSessionStatus.Stopping,
                CollectorSessionStatus.Invalidating
            ]);
    }

    [Fact]
    public async Task GetCurrentByMarketIdAsync_WithActiveSession_ShouldPreferActiveSession()
    {
        var marketId = MarketId.Create(Guid.NewGuid()).Value;
        var active = CreateSession(marketId, Now);
        MarkRunning(active, Now.AddSeconds(1));
        var newerStopped = CreateSession(marketId, Now.AddMinutes(1));
        newerStopped.Stop(Now.AddMinutes(2), CollectorStopReason.Requested);
        await using var context = new DataCollectionDbContext(
            CreateOptions(new InMemoryDatabaseRoot()));
        context.CollectorSessions.AddRange(active, newerStopped);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new CollectorSessionRepository(context);

        var current = await repository.GetCurrentByMarketIdAsync(
            marketId,
            CancellationToken.None);

        current!.Id.Should().Be(active.Id);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task GetCurrentByMarketIdAsync_WithoutActiveSession_ShouldReturnLatestSession()
    {
        var marketId = MarketId.Create(Guid.NewGuid()).Value;
        var older = CreateSession(marketId, Now);
        older.Stop(Now.AddSeconds(1), CollectorStopReason.Requested);
        var latest = CreateSession(marketId, Now.AddMinutes(1));
        latest.Fail(
            Now.AddMinutes(2),
            CollectorStopReason.StartupFailure,
            "collector.start.failed",
            "Start failed.");
        var otherMarket = CreateSession();
        await using var context = new DataCollectionDbContext(
            CreateOptions(new InMemoryDatabaseRoot()));
        context.CollectorSessions.AddRange(older, latest, otherMarket);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new CollectorSessionRepository(context);

        var current = await repository.GetCurrentByMarketIdAsync(
            marketId,
            CancellationToken.None);

        current!.Id.Should().Be(latest.Id);
    }

    [Fact]
    public async Task GetCurrentByMarketIdAsync_WithoutSessions_ShouldReturnNull()
    {
        await using var context = new DataCollectionDbContext(
            CreateOptions(new InMemoryDatabaseRoot()));
        var repository = new CollectorSessionRepository(context);

        var current = await repository.GetCurrentByMarketIdAsync(
            MarketId.Create(Guid.NewGuid()).Value,
            CancellationToken.None);

        current.Should().BeNull();
    }

    private static DbContextOptions<DataCollectionDbContext> CreateOptions(
        InMemoryDatabaseRoot databaseRoot)
    {
        return new DbContextOptionsBuilder<DataCollectionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), databaseRoot)
            .Options;
    }

    private static CollectorSessionAggregate CreateSession(
        MarketId? marketId = null,
        DateTimeOffset? createdAt = null)
    {
        return CollectorSessionAggregate.Create(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            marketId ?? MarketId.Create(Guid.NewGuid()).Value,
            "event-123",
            "btc-updown-5m-1200",
            "market-123",
            "btc-updown-5m-1200",
            "0xabc",
            (createdAt ?? Now).AddMinutes(3),
            (createdAt ?? Now).AddMinutes(8),
            3,
            [
                new CollectorSessionTokenDefinition(TokenId.Create("1001").Value, "Yes", 0),
                new CollectorSessionTokenDefinition(TokenId.Create("1002").Value, "No", 1)
            ],
            createdAt ?? Now).Value;
    }

    private static void MarkRunning(
        CollectorSessionAggregate session,
        DateTimeOffset readyAt)
    {
        session.BeginPreparation(session.CreatedAt);
        session.MarkAwaitingInitialBooks();
        session.MarkAwaitingHeartbeat();
        session.MarkRunning(readyAt);
    }
}
