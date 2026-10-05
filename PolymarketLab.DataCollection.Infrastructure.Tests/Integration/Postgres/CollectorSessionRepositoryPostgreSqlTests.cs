using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Domain.Models.Resolution;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.CollectorSession;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Integration.Postgres;

[Collection(PostgreSqlCollection.Name)]
public sealed class CollectorSessionRepositoryPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 27, 11, 57, 0, TimeSpan.Zero);

    [Fact]
    public async Task ConcurrentInserts_ForDifferentMarkets_ShouldAllowBothSessions()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var first = CreateSession(MarketId.Create(Guid.NewGuid()).Value);
        var second = CreateSession(MarketId.Create(Guid.NewGuid()).Value);

        var results = await Task.WhenAll(
            InsertAsync(database.ConnectionString, first),
            InsertAsync(database.ConnectionString, second));

        results.Should().OnlyContain(result =>
            result == CollectorSessionInsertStatus.Inserted);
        await using var context = CreateContext(database.ConnectionString);
        var active = await new CollectorSessionRepository(context)
            .GetActiveAsync(CancellationToken.None);
        active.Should().HaveCount(2);
        active.Select(session => session.Id).Should().Contain([first.Id, second.Id]);
    }

    [Fact]
    public async Task ConcurrentInserts_ForSameMarket_ShouldAllowOneIdempotentWinner()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var marketId = MarketId.Create(Guid.NewGuid()).Value;
        var first = CreateSession(marketId);
        var second = CreateSession(marketId);

        var results = await Task.WhenAll(
            InsertAsync(database.ConnectionString, first),
            InsertAsync(database.ConnectionString, second));

        results.Should().ContainSingle(result =>
            result == CollectorSessionInsertStatus.Inserted);
        results.Should().ContainSingle(result =>
            result == CollectorSessionInsertStatus.ActiveMarketConflict);
        await using var context = CreateContext(database.ConnectionString);
        var active = await new CollectorSessionRepository(context)
            .GetActiveByMarketIdAsync(marketId, CancellationToken.None);
        active.Should().NotBeNull();
        active!.MarketId.Should().Be(marketId);
    }

    [Fact]
    public async Task TerminalSession_ShouldReleaseMarketSlot()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var first = CreateSession(MarketId.Create(Guid.NewGuid()).Value);
        await using (var firstContext = CreateContext(database.ConnectionString))
        {
            var repository = new CollectorSessionRepository(firstContext);
            (await repository.TryAddAsync(first, CancellationToken.None)).Value
                .Should().Be(CollectorSessionInsertStatus.Inserted);
            first.Stop(CreatedAt.AddMinutes(1), CollectorStopReason.MarketClosed);
            (await repository.TryUpdateAsync(
                first,
                CollectorSessionStatus.Scheduled,
                CancellationToken.None)).Value.Should().Be(CollectorSessionUpdateStatus.Updated);
        }

        var second = CreateSession(MarketId.Create(Guid.NewGuid()).Value);
        var result = await InsertAsync(database.ConnectionString, second);

        result.Should().Be(CollectorSessionInsertStatus.Inserted);
    }

    [Fact]
    public async Task TryUpdateAsync_AfterDetachedRead_ShouldPreserveActiveMarketIndex()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var session = CreateSession(MarketId.Create(Guid.NewGuid()).Value);
        await InsertAsync(database.ConnectionString, session);
        await using var context = CreateContext(database.ConnectionString);
        var repository = new CollectorSessionRepository(context);
        var persisted = await repository.GetByIdAsync(session.Id, CancellationToken.None);
        persisted!.BeginPreparation(CreatedAt).IsSuccess.Should().BeTrue();

        var result = await repository.TryUpdateAsync(
            persisted,
            CollectorSessionStatus.Scheduled,
            CancellationToken.None);

        result.Value.Should().Be(CollectorSessionUpdateStatus.Updated);
        await using var verificationContext = CreateContext(database.ConnectionString);
        var updated = await new CollectorSessionRepository(verificationContext)
            .GetByIdAsync(session.Id, CancellationToken.None);
        updated!.Status.Should().Be(CollectorSessionStatus.Starting);
    }

    [Fact]
    public async Task TryUpdateAsync_WithAwaitingNormalization_ShouldPersistDeadlineAnchor()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var session = CreateSession(MarketId.Create(Guid.NewGuid()).Value);
        await InsertAsync(database.ConnectionString, session);
        await using var context = CreateContext(database.ConnectionString);
        var repository = new CollectorSessionRepository(context);
        var persisted = await repository.GetByIdAsync(session.Id, CancellationToken.None);
        persisted!.BeginPreparation(CreatedAt).IsSuccess.Should().BeTrue();
        persisted.MarkNewConnectionEpoch().IsSuccess.Should().BeTrue();
        persisted.MarkAwaitingHeartbeat().IsSuccess.Should().BeTrue();
        persisted.MarkRunning(CreatedAt.AddMinutes(2)).IsSuccess.Should().BeTrue();
        persisted.MarkCollectingWindow().IsSuccess.Should().BeTrue();
        persisted.MarkAwaitingResolution().IsSuccess.Should().BeTrue();
        persisted.ConfirmResolution(
            persisted.EventEndsAt!.Value,
            persisted.EventEndsAt.Value.AddSeconds(1),
            new ResolutionWinner("1001", "Yes"),
            1).IsSuccess.Should().BeTrue();
        persisted.MarkStopping().IsSuccess.Should().BeTrue();
        var awaitingNormalizationAt = persisted.EventEndsAt.Value.AddSeconds(3);
        persisted.MarkAwaitingNormalization(awaitingNormalizationAt)
            .IsSuccess.Should().BeTrue();

        var update = await repository.TryUpdateAsync(
            persisted,
            CollectorSessionStatus.Scheduled,
            CancellationToken.None);

        update.Value.Should().Be(CollectorSessionUpdateStatus.Updated);
        await using var verificationContext = CreateContext(database.ConnectionString);
        var reloaded = await new CollectorSessionRepository(verificationContext)
            .GetByIdAsync(session.Id, CancellationToken.None);
        reloaded!.Status.Should().Be(CollectorSessionStatus.Stopping);
        reloaded.Phase.Should().Be(CollectorSessionPhase.AwaitingNormalization);
        reloaded.AwaitingNormalizationAt.Should().Be(awaitingNormalizationAt);
    }

    [Fact]
    public async Task TryUpdateAsync_AfterConcurrencyConflict_ShouldAllowRetryInSameContext()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var session = CreateSession(MarketId.Create(Guid.NewGuid()).Value);
        await InsertAsync(database.ConnectionString, session);
        await using var staleContext = CreateContext(database.ConnectionString);
        var staleRepository = new CollectorSessionRepository(staleContext);
        var stale = await staleRepository.GetByIdAsync(session.Id, CancellationToken.None);

        await using (var winningContext = CreateContext(database.ConnectionString))
        {
            var winningRepository = new CollectorSessionRepository(winningContext);
            var winning = await winningRepository.GetByIdAsync(session.Id, CancellationToken.None);
            winning!.BeginPreparation(CreatedAt).IsSuccess.Should().BeTrue();
            (await winningRepository.TryUpdateAsync(
                winning,
                CollectorSessionStatus.Scheduled,
                CancellationToken.None)).Value.Should().Be(CollectorSessionUpdateStatus.Updated);
        }

        stale!.Interrupt(CreatedAt, CollectorStopReason.ProcessTerminated)
            .IsSuccess.Should().BeTrue();
        (await staleRepository.TryUpdateAsync(
            stale,
            CollectorSessionStatus.Scheduled,
            CancellationToken.None)).Value.Should().Be(
                CollectorSessionUpdateStatus.ConcurrencyConflict);
        var refreshed = await staleRepository.GetByIdAsync(
            session.Id,
            CancellationToken.None);
        refreshed!.MarkStopping().IsSuccess.Should().BeTrue();

        var retry = await staleRepository.TryUpdateAsync(
            refreshed,
            CollectorSessionStatus.Starting,
            CancellationToken.None);

        retry.Value.Should().Be(CollectorSessionUpdateStatus.Updated);
    }

    [Fact]
    public async Task Read_ShouldRestoreExactSnapshotAndTokenOrder()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var session = CreateSession(MarketId.Create(Guid.NewGuid()).Value);
        await InsertAsync(database.ConnectionString, session);
        await using var context = CreateContext(database.ConnectionString);

        var persisted = await new CollectorSessionRepository(context)
            .GetByIdAsync(session.Id, CancellationToken.None);

        persisted.Should().NotBeNull();
        persisted!.ConditionId.Should().Be("0xabc");
        persisted.ProjectionVersion.Should().Be(3);
        persisted.Tokens.Select(token => token.TokenId.Value)
            .Should().Equal("1001", "1002");
    }

    [Fact]
    public async Task ConcurrentDiagnosticInserts_ShouldAdmitAtMostConfiguredQuota()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var sessions = Enumerable.Range(0, 6)
            .Select(_ => CreateSession(
                MarketId.Create(Guid.NewGuid()).Value,
                CollectorFailurePolicy.RetainOnFailure))
            .ToArray();

        var results = await Task.WhenAll(sessions.Select(session =>
            InsertAsync(database.ConnectionString, session, maximumRetainedSessions: 5)));

        results.Count(result => result == CollectorSessionInsertStatus.Inserted)
            .Should().Be(5);
        results.Should().ContainSingle(result =>
            result == CollectorSessionInsertStatus.DiagnosticQuotaExceeded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalDiagnosticSession_ShouldReleaseQuota(bool failedAndDeleted)
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var sessions = Enumerable.Range(0, 5)
            .Select(_ => CreateSession(
                MarketId.Create(Guid.NewGuid()).Value,
                CollectorFailurePolicy.RetainOnFailure))
            .ToArray();
        foreach (var session in sessions)
        {
            (await InsertAsync(database.ConnectionString, session, 5)).Should()
                .Be(CollectorSessionInsertStatus.Inserted);
        }

        await using (var context = CreateContext(database.ConnectionString))
        {
            var repository = CreateRepository(context, 5);
            var released = await repository.GetByIdAsync(
                sessions[0].Id,
                CancellationToken.None);
            if (failedAndDeleted)
            {
                released!.BeginInvalidation(
                    CreatedAt.AddMinutes(1),
                    CollectorStopReason.StartupFailure,
                    "collector.start.failed",
                    "Start failed.").IsSuccess.Should().BeTrue();
                released.CompleteInvalidation(CreatedAt.AddMinutes(1))
                    .IsSuccess.Should().BeTrue();
                released.CompleteRetentionExpiry(CreatedAt.AddHours(13))
                    .IsSuccess.Should().BeTrue();
            }
            else
            {
                released!.Stop(
                    CreatedAt.AddMinutes(1),
                    CollectorStopReason.Requested).IsSuccess.Should().BeTrue();
            }

            (await repository.TryUpdateAsync(
                released,
                CollectorSessionStatus.Scheduled,
                CancellationToken.None)).Value.Should().Be(
                    CollectorSessionUpdateStatus.Updated);
        }

        var replacement = CreateSession(
            MarketId.Create(Guid.NewGuid()).Value,
            CollectorFailurePolicy.RetainOnFailure);
        (await InsertAsync(database.ConnectionString, replacement, 5)).Should()
            .Be(CollectorSessionInsertStatus.Inserted);
    }

    [Fact]
    public async Task StandardInsert_WhenDiagnosticQuotaIsFull_ShouldStillBeInserted()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        for (var index = 0; index < 5; index++)
        {
            var diagnostic = CreateSession(
                MarketId.Create(Guid.NewGuid()).Value,
                CollectorFailurePolicy.RetainOnFailure);
            (await InsertAsync(database.ConnectionString, diagnostic, 5)).Should()
                .Be(CollectorSessionInsertStatus.Inserted);
        }

        var standard = CreateSession(MarketId.Create(Guid.NewGuid()).Value);

        (await InsertAsync(database.ConnectionString, standard, 5)).Should()
            .Be(CollectorSessionInsertStatus.Inserted);
    }

    [Fact]
    public async Task GetExpiredRetainedAsync_ShouldReturnOnlyExpiredFailedRetainedSessions()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var expired = CreateSession(
            MarketId.Create(Guid.NewGuid()).Value,
            CollectorFailurePolicy.RetainOnFailure);
        var future = CreateSession(
            MarketId.Create(Guid.NewGuid()).Value,
            CollectorFailurePolicy.RetainOnFailure);
        var deleted = CreateSession(MarketId.Create(Guid.NewGuid()).Value);
        expired.BeginInvalidation(
            CreatedAt.AddMinutes(1),
            CollectorStopReason.StartupFailure,
            "collector.start.failed",
            "Start failed.");
        expired.CompleteInvalidation(CreatedAt.AddMinutes(1));
        future.BeginInvalidation(
            CreatedAt.AddHours(2),
            CollectorStopReason.StartupFailure,
            "collector.start.failed",
            "Start failed.");
        future.CompleteInvalidation(CreatedAt.AddHours(2));
        deleted.Fail(
            CreatedAt.AddMinutes(1),
            CollectorStopReason.StartupFailure,
            "collector.start.failed",
            "Start failed.");
        await using (var context = CreateContext(database.ConnectionString))
        {
            context.CollectorSessions.AddRange(expired, future, deleted);
            await context.SaveChangesAsync();
        }

        await using var readContext = CreateContext(database.ConnectionString);
        var sessions = await CreateRepository(readContext, 5)
            .GetExpiredRetainedAsync(CreatedAt.AddHours(13), CancellationToken.None);

        sessions.Select(session => session.Id).Should().Equal(expired.Id);
    }

    private async Task<PostgreSqlTestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await fixture.CreateDatabaseAsync();
        await using var context = CreateContext(database.ConnectionString);
        await context.Database.MigrateAsync();
        return database;
    }

    private static async Task<CollectorSessionInsertStatus> InsertAsync(
        string connectionString,
        CollectorSessionAggregate session,
        int? maximumRetainedSessions = null)
    {
        await using var context = CreateContext(connectionString);
        var repository = maximumRetainedSessions is null
            ? new CollectorSessionRepository(context)
            : CreateRepository(context, maximumRetainedSessions.Value);
        var result = await repository
            .TryAddAsync(session, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static DataCollectionDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<DataCollectionDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new DataCollectionDbContext(options);
    }

    private static CollectorSessionRepository CreateRepository(
        DataCollectionDbContext context,
        int maximumRetainedSessions) =>
        new(
            context,
            Options.Create(new FailedDatasetRetentionOptions
            {
                MaximumRetainedSessions = maximumRetainedSessions
            }));

    private static CollectorSessionAggregate CreateSession(
        MarketId marketId,
        CollectorFailurePolicy failurePolicy = CollectorFailurePolicy.DeleteOnFailure) =>
        CollectorSessionAggregate.Create(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            marketId,
            "event-123",
            "btc-updown-5m-1200",
            "market-123",
            "btc-updown-5m-1200",
            "0xabc",
            CreatedAt.AddMinutes(3),
            CreatedAt.AddMinutes(8),
            3,
            [
                new CollectorSessionTokenDefinition(TokenId.Create("1001").Value, "Yes", 0),
                new CollectorSessionTokenDefinition(TokenId.Create("1002").Value, "No", 1)
            ],
            CreatedAt,
            failurePolicy,
            failurePolicy == CollectorFailurePolicy.RetainOnFailure
                ? TimeSpan.FromHours(12)
                : null).Value;
}
