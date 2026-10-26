using System.Data.Common;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection;
using PolymarketLab.DataCollection.Core.Application.Simulation;
using PolymarketLab.DataCollection.Core.Application.Simulation.Execution;
using PolymarketLab.DataCollection.Core.Application.Simulation.Rules;
using PolymarketLab.DataCollection.Core.Application.Simulation.RunExperiment;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorOrderBookIntegrity;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Simulation;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.CollectorSession;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Normalization;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;
using Session = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Integration.Postgres;

[Collection(PostgreSqlCollection.Name)]
public sealed class SimulationExperimentPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Start = new(2026, 10, 16, 9, 20, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReadAndRun_ShouldUsePinnedVersionAndLeaveArchiveUnchanged()
    {
        await using var database = await SeedAsync();
        await using var context = Context(database.ConnectionString);
        var id = (await context.CollectorSessions.SingleAsync()).Id;
        var guard = new ReadOnlyGuard();
        await using var readContext = Context(database.ConnectionString, guard);
        var reader = CreateReader(readContext);
        var definition = Definition();
        var selection = ExperimentRequestBuilder.Create(id, 1, definition);
        var first = (await reader.ReadAsync(selection, default)).Value;
        first.RawMessages.Should().HaveCount(2);
        first.OrderBookEvents.Should().HaveCount(2);
        guard.ObservedReadOnly.Should().BeTrue();

        var handler = new RunExperimentHandler(reader, new ExperimentRunner(new OrderBookProjector()));
        var command = new RunExperimentCommand(id.Value, 1, first.Manifest, definition);
        var run1 = (await handler.Handle(command, default)).Value;
        var run2 = (await handler.Handle(command, default)).Value;
        SimulationJson.Serialize(run1).Should().Be(SimulationJson.Serialize(run2));
        run1.Balance.Positions[0].Quantity.Should().Be(5);
        run1.Balance.FeesPaid.Should().Be(0.024m);
        run1.SettlementStatus.Should().Be("Unconfirmed");
        (await reader.ReadAsync(selection, default)).Value.Manifest.ManifestFingerprint.Should().Be(first.Manifest.ManifestFingerprint);
        (await context.RawMarketMessages.CountAsync()).Should().Be(2);
        (await context.CollectorSessions.AsNoTracking().SingleAsync()).Status.Should()
            .Be(Core.Domain.Models.Enums.CollectorSessionStatus.Stopped);

        await context.Database.ExecuteSqlRawAsync("UPDATE data_collection.raw_market_messages SET payload = convert_to('changed', 'UTF8')");
        (await handler.Handle(command, default)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Read_MissingLedgerOrWrongVersion_ShouldReturnExpectedFailure()
    {
        await using var database = await SeedAsync();
        await using var context = Context(database.ConnectionString);
        var id = (await context.CollectorSessions.SingleAsync()).Id;
        var reader = CreateReader(context);
        (await reader.ReadAsync(ExperimentRequestBuilder.Create(id, 2, Definition()), default)).IsFailure.Should().BeTrue();
        await context.Database.ExecuteSqlRawAsync("DELETE FROM data_collection.raw_message_normalizations WHERE raw_message_id = (SELECT max(id) FROM data_collection.raw_market_messages)");
        var result = await reader.ReadAsync(ExperimentRequestBuilder.Create(id, 1, Definition()), default);
        result.Value.Suitability.NormalizationComplete.Should().BeFalse();
        result.Value.Suitability.IsArchiveUsable.Should().BeFalse();
    }

    [Fact]
    public async Task Read_ConcurrentProjectionChange_ShouldKeepOneRepeatableReadSnapshot()
    {
        await using var database = await SeedAsync();
        await using var normalContext = Context(database.ConnectionString);
        var id = (await normalContext.CollectorSessions.SingleAsync()).Id;
        var selection = ExperimentRequestBuilder.Create(id, 1, Definition());
        var first = (await CreateReader(normalContext).ReadAsync(selection, default)).Value;
        var interceptor = new ChangeLevelsBeforeRead(async () =>
        {
            await using var writer = Context(database.ConnectionString);
            await writer.Database.ExecuteSqlRawAsync("UPDATE data_collection.book_levels SET size = 7 WHERE side = 2");
        });
        await using var concurrentContext = Context(database.ConnectionString, interceptor);
        var concurrent = (await CreateReader(concurrentContext).ReadAsync(selection, default)).Value;
        concurrent.Manifest.ManifestFingerprint.Should().Be(first.Manifest.ManifestFingerprint);
        var later = (await CreateReader(normalContext).ReadAsync(selection, default)).Value;
        later.Manifest.RawFingerprint.Should().Be(first.Manifest.RawFingerprint);
        later.Manifest.ProjectionFingerprint.Should().NotBe(first.Manifest.ProjectionFingerprint);
    }

    private async Task<PostgreSqlTestDatabase> SeedAsync()
    {
        var database = await fixture.CreateDatabaseAsync();
        await using var context = Context(database.ConnectionString);
        await context.Database.MigrateAsync();
        var session = Session.Create(CollectorSessionId.Create(Guid.NewGuid()).Value,
            MarketId.Create(Guid.NewGuid()).Value, "event", "event-slug", "market", "market-slug", "condition",
            Start, Start.AddMinutes(5), 1,
            [new CollectorSessionTokenDefinition(TokenId.Create("up").Value, "Up", 0),
             new CollectorSessionTokenDefinition(TokenId.Create("down").Value, "Down", 1)], Start.AddMinutes(-2)).Value;
        context.CollectorSessions.Add(session);
        var at = Start.AddSeconds(-1);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new[] { "up", "down" }.Select(asset => new
        {
            event_type = "book", market = "condition", asset_id = asset,
            timestamp = "1000", hash = "hash", tick_size = "0.01",
            bids = new[] { new { price = "0.40", size = "10" } },
            asks = new[] { new { price = "0.48", size = "10" } }
        }));
        var raw = new RawMarketMessageRecord(session.Id, 1, at, payload);
        var empty = new RawMarketMessageRecord(session.Id, 1, at.AddMilliseconds(500), "[]"u8.ToArray());
        context.RawMarketMessages.AddRange(raw, empty);
        await context.SaveChangesAsync();
        foreach (var (asset, index) in new[] { ("up", 0), ("down", 1) })
        {
            var book = new BookSnapshotRecord("hash", 0.01m, null);
            var header = new NormalizedEventRecord(new NormalizedEvent(raw.Id, index, 1, 1, "book",
                session.Id, at, 1000, "condition", asset, [book]), at);
            context.NormalizedEvents.Add(header);
            await context.SaveChangesAsync();
            context.BookSnapshots.Add(new BookSnapshotEntity(header.Id, book));
            context.BookLevels.AddRange(
                new BookLevelEntity(header.Id, new BookLevelRecord(OrderBookSide.Bid, 0, 0.40m, 10)),
                new BookLevelEntity(header.Id, new BookLevelRecord(OrderBookSide.Ask, 0, 0.48m, 10)));
        }
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE data_collection.collector_sessions SET status = 3, stop_reason = 3 WHERE id = {session.Id.Value}");
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO data_collection.raw_message_normalizations (raw_message_id, projection_version, status, attempt_count, completed_at) VALUES ({raw.Id}, 1, 3, 1, {Start}), ({empty.Id}, 1, 3, 1, {Start}), ({raw.Id}, 2, 5, 1, {Start})");
        return database;
    }

    private static DataCollectionDbContext Context(string connectionString, IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<DataCollectionDbContext>().UseNpgsql(connectionString);
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        return new(builder.Options);
    }

    private static ExperimentDefinition Definition()
    {
        var rules = TradingRulesProfile.CreateExperimental("test-only", "synthetic fixed fee",
            Start.AddDays(-1), Start.AddDays(-1), 1, 1, 0.000001m, 100).Value;
        return new ExperimentDefinition(1, 100, Start.AddMinutes(5),
            [new ScheduledOrder(Start, new SimulatedOrder(1, "up", TradeSide.Buy, 5, 0.48m, TimeSpan.Zero))],
            [rules], [], null, false, [], null);
    }

    private static ExperimentDatasetReader CreateReader(DataCollectionDbContext context) =>
        new(context, new CollectorSessionRepository(context), new NormalizationSuitabilityReader(context),
            new OrderBookIntegrityReader(context), new CollectorOrderBookIntegrityCoordinator(
                new OrderBookIntegrityReader(context), new OrderBookProjector(),
                NullLogger<CollectorOrderBookIntegrityCoordinator>.Instance), NullLogger<ExperimentDatasetReader>.Instance);

    private sealed class ReadOnlyGuard : DbCommandInterceptor
    {
        public bool ObservedReadOnly { get; private set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            await using var check = command.Connection!.CreateCommand();
            check.Transaction = command.Transaction;
            check.CommandText = "SELECT current_setting('transaction_read_only')";
            ((string)(await check.ExecuteScalarAsync(cancellationToken))!).Should().Be("on");
            ObservedReadOnly = true;
            return result;
        }
    }

    private sealed class ChangeLevelsBeforeRead(Func<Task> change) : DbCommandInterceptor
    {
        private bool _changed;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (!_changed && command.CommandText.Contains("book_levels", StringComparison.Ordinal))
            {
                _changed = true;
                await change();
            }
            return result;
        }
    }
}
