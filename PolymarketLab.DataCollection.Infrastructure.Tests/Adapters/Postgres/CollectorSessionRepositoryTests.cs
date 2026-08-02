using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports.Enums;
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
        runningSession!.MarkRunning(Now.AddSeconds(1));
        failedSession!.Fail(
            Now.AddSeconds(2),
            CollectorStopReason.FatalWebSocketError,
            "collector.runtime.receive.failed",
            "Receive failed.");

        var failedUpdate = await failedRepository.TryUpdateAsync(
            failedSession,
            CollectorSessionStatus.Starting,
            CancellationToken.None);
        var staleRunningUpdate = await runningRepository.TryUpdateAsync(
            runningSession,
            CollectorSessionStatus.Starting,
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
    public async Task GetActiveAsync_ShouldReturnOnlyActiveStatuses()
    {
        var options = CreateOptions(new InMemoryDatabaseRoot());
        var starting = CreateSession();
        var running = CreateSession();
        running.MarkRunning(Now.AddSeconds(1));
        var stopping = CreateSession();
        stopping.MarkRunning(Now.AddSeconds(1));
        stopping.MarkStopping();
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
            starting,
            running,
            stopping,
            stopped,
            failed,
            interrupted);
        await context.SaveChangesAsync();
        var repository = new CollectorSessionRepository(context);

        var activeSessions = await repository.GetActiveAsync(
            CancellationToken.None);

        activeSessions.Select(session => session.Status).Should().BeEquivalentTo(
            [
                CollectorSessionStatus.Starting,
                CollectorSessionStatus.Running,
                CollectorSessionStatus.Stopping
            ]);
    }

    private static DbContextOptions<DataCollectionDbContext> CreateOptions(
        InMemoryDatabaseRoot databaseRoot)
    {
        return new DbContextOptionsBuilder<DataCollectionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), databaseRoot)
            .Options;
    }

    private static CollectorSessionAggregate CreateSession()
    {
        return CollectorSessionAggregate.Create(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            MarketId.Create(Guid.NewGuid()).Value,
            Now).Value;
    }
}
