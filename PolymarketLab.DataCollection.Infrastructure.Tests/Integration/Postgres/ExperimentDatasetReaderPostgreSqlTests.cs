using System.Collections.Immutable;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Npgsql;
using PolymarketLab.DataCollection.Core.Application.Simulation.Datasets;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorOrderBookIntegrity;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.CollectorSession;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Normalization;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Simulation;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Integration.Postgres;

[Collection(PostgreSqlCollection.Name)]
public sealed class ExperimentDatasetReaderPostgreSqlTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task ReadAsync_ShouldPinExactSessionAndRepeatManifestWithoutInventingMetadata()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = CreateContext(database.ConnectionString);
        await context.Database.MigrateAsync();
        var sessionId = Guid.NewGuid();
        await SeedAsync(database.ConnectionString, sessionId);
        await SeedAsync(database.ConnectionString, Guid.NewGuid());
        await ExecuteAsync(database.ConnectionString, """
            UPDATE data_collection.collector_sessions SET
                market_id = (SELECT market_id FROM data_collection.collector_sessions WHERE id = @session),
                created_at = created_at + interval '1 second'
            WHERE id <> @session
            """, sessionId);
        var request = new ExperimentDatasetRequest(CollectorSessionId.Create(sessionId).Value, 1);
        var reader = CreateReader(context);

        var first = await reader.ReadAsync(request, CancellationToken.None);
        var second = await reader.ReadAsync(request, CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        first.Value.Manifest.Session.SessionId.Should().Be(sessionId);
        first.Value.Manifest.Session.Tokens.Select(token => token.Outcome).Should().Equal("Up", "Down");
        first.Value.Manifest.RawCount.Should().Be(1);
        first.Value.Manifest.ManifestFingerprint.Should().Be(second.Value.Manifest.ManifestFingerprint);
        first.Value.Suitability.NormalizationComplete.Should().BeTrue();
        first.Value.Suitability.Resolution.Should().Be(DatasetKnowledge.Missing);
        first.Value.Suitability.Fees.Should().Be(DatasetKnowledge.Missing);
        first.Value.Suitability.MinimumOrderSize.Should().Be(DatasetKnowledge.Missing);
        first.Value.Suitability.Btc.Should().Be(DatasetKnowledge.Missing);
        first.Value.Manifest.Session.ResolutionConfirmedAt.Should().BeNull();
        // Обработанный [] не требует события, но не заменяет initial book обоих исходов.
        first.Value.ProjectionItems.Should().BeEmpty();
        first.Value.Suitability.TerminalOrderBookIntegrity.Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_WithInitialBooksAndProcessedEmptyArray_ShouldReturnUsableImmutableArchive()
    {
        await using var database = await CreateDatabaseAsync();
        var id = Guid.NewGuid();
        await SeedAsync(database.ConnectionString, id);
        await SeedBooksAsync(database.ConnectionString, id);
        await using var context = CreateContext(database.ConnectionString);

        var result = await CreateReader(context).ReadAsync(Request(id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Normalization.RawCount.Should().Be(3);
        result.Value.ProjectionItems.Should().HaveCount(2);
        result.Value.Suitability.IsArchiveUsable.Should().BeTrue();
        result.Value.OrderBookEvents.Should().HaveCount(2);
        var book = (NormalizedOrderBookEvent.BookSnapshot)result.Value.OrderBookEvents[0];
        var mutate = () => ((IList<PolymarketLab.DataCollection.Core.Application.Normalization.Models.BookLevelRecord>)book.Record.Bids).Clear();
        mutate.Should().Throw<NotSupportedException>();
        var receivedAt = book.Record.ReceivedAt;
        receivedAt.Should().BeBefore(result.Value.Manifest.Session.EventStartsAt!.Value);
    }

    [Theory]
    [InlineData("missing", "DELETE FROM data_collection.raw_message_normalizations WHERE raw_message_id = (SELECT min(id) FROM data_collection.raw_market_messages WHERE session_id = @session)")]
    [InlineData("processing", "UPDATE data_collection.raw_message_normalizations SET status = 2 WHERE raw_message_id IN (SELECT id FROM data_collection.raw_market_messages WHERE session_id = @session)")]
    [InlineData("completed_at", "UPDATE data_collection.raw_message_normalizations SET completed_at = NULL WHERE raw_message_id IN (SELECT id FROM data_collection.raw_market_messages WHERE session_id = @session)")]
    public async Task ReadAsync_WithIncompleteLedger_ShouldBlockReplay(string kind, string mutation)
    {
        await using var database = await CreateDatabaseAsync();
        var id = Guid.NewGuid();
        await SeedAsync(database.ConnectionString, id);
        await ExecuteAsync(database.ConnectionString, mutation, id);
        await using var context = CreateContext(database.ConnectionString);

        var result = await CreateReader(context).ReadAsync(Request(id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Suitability.NormalizationComplete.Should().BeFalse();
        result.Value.Suitability.IsArchiveUsable.Should().BeFalse();
        result.Value.Suitability.TerminalOrderBookIntegrity.Should().BeNull();
        result.Value.Suitability.Diagnostics.Should().Contain(item => item.Code == "experiment.dataset.normalization_incomplete");
        if (kind == "missing") result.Value.Normalization.MissingCount.Should().Be(1);
    }

    [Fact]
    public async Task ReadAsync_ShouldIgnoreOtherSessionAndProjectionVersion()
    {
        await using var database = await CreateDatabaseAsync();
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();
        await SeedAsync(database.ConnectionString, id);
        await SeedBooksAsync(database.ConnectionString, id);
        await using var context = CreateContext(database.ConnectionString);
        var reader = CreateReader(context);
        var before = await reader.ReadAsync(Request(id), CancellationToken.None);
        await SeedAsync(database.ConnectionString, other);
        await SeedBooksAsync(database.ConnectionString, other);
        await ExecuteAsync(database.ConnectionString, """
            INSERT INTO data_collection.raw_message_normalizations
                (raw_message_id, projection_version, status, attempt_count)
            SELECT id, 2, 5, 1 FROM data_collection.raw_market_messages WHERE session_id = @session;
            INSERT INTO data_collection.normalized_events
                (raw_message_id, raw_item_index, projection_version, normalizer_version,
                 event_type, session_id, received_at, normalized_at)
            SELECT id, 10, 2, 1, 'book', session_id, received_at, received_at
            FROM data_collection.raw_market_messages WHERE session_id = @session;
            """, id);

        var after = await reader.ReadAsync(Request(id), CancellationToken.None);
        var wrongVersion = await reader.ReadAsync(Request(id) with { ProjectionVersion = 2 }, CancellationToken.None);

        after.Value.Manifest.ManifestFingerprint.Should().Be(before.Value.Manifest.ManifestFingerprint);
        after.Value.Suitability.IsArchiveUsable.Should().BeTrue();
        wrongVersion.IsFailure.Should().BeTrue();
        wrongVersion.Error.Code.Should().Be("experiment.dataset.projection_version_mismatch");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadAsync_WithCrossSessionProjection_ShouldDiagnoseBothDirections(bool corruptSelectedHeader)
    {
        await using var database = await CreateDatabaseAsync();
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();
        await SeedAsync(database.ConnectionString, id);
        await SeedBooksAsync(database.ConnectionString, id);
        await SeedAsync(database.ConnectionString, other);
        await SeedBooksAsync(database.ConnectionString, other);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE data_collection.normalized_events SET session_id = @target
            WHERE id = (SELECT min(id) FROM data_collection.normalized_events WHERE session_id = @source)
            """;
        command.Parameters.AddWithValue("source", corruptSelectedHeader ? id : other);
        command.Parameters.AddWithValue("target", corruptSelectedHeader ? other : id);
        await command.ExecuteNonQueryAsync();
        await using var context = CreateContext(database.ConnectionString);

        var result = await CreateReader(context).ReadAsync(Request(id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Suitability.ProjectionConsistent.Should().BeFalse();
        result.Value.Suitability.IsArchiveUsable.Should().BeFalse();
        result.Value.OrderBookEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData("DELETE FROM data_collection.book_snapshots WHERE event_id IN (SELECT id FROM data_collection.normalized_events WHERE session_id = @session)")]
    [InlineData("UPDATE data_collection.normalized_events SET normalizer_version = 99 WHERE session_id = @session")]
    [InlineData("UPDATE data_collection.normalized_events SET asset_id = 'foreign' WHERE session_id = @session")]
    public async Task ReadAsync_WithInvalidTypedProjection_ShouldReturnReportInsteadOfThrowing(string mutation)
    {
        await using var database = await CreateDatabaseAsync();
        var id = Guid.NewGuid();
        await SeedAsync(database.ConnectionString, id);
        await SeedBooksAsync(database.ConnectionString, id);
        await ExecuteAsync(database.ConnectionString, mutation, id);
        await using var context = CreateContext(database.ConnectionString);

        var result = await CreateReader(context).ReadAsync(Request(id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Suitability.ProjectionConsistent.Should().BeFalse();
        result.Value.Suitability.Diagnostics.Should().Contain(item => item.Code == "experiment.dataset.projection_inconsistent");
    }

    [Fact]
    public async Task ReadAsync_ShouldDetectRawTypedMetadataAndSettingsChangesIndependently()
    {
        await using var database = await CreateDatabaseAsync();
        var id = Guid.NewGuid();
        await SeedAsync(database.ConnectionString, id);
        await SeedBooksAsync(database.ConnectionString, id);
        await using var context = CreateContext(database.ConnectionString);
        var reader = CreateReader(context);
        var request = Request(id);
        var before = (await reader.ReadAsync(request, CancellationToken.None)).Value.Manifest;

        await ExecuteAsync(database.ConnectionString,
            "UPDATE data_collection.raw_market_messages SET payload = payload || decode('20', 'hex') WHERE session_id = @session", id);
        var rawChanged = (await reader.ReadAsync(request, CancellationToken.None)).Value.Manifest;
        rawChanged.RawFingerprint.Should().NotBe(before.RawFingerprint);
        var rawPinned = await reader.ReadAsync(request with { ExpectedRawFingerprint = before.RawFingerprint }, CancellationToken.None);
        rawPinned.Error.Code.Should().Be("experiment.dataset.raw_fingerprint_mismatch");

        await ExecuteAsync(database.ConnectionString, """
            UPDATE data_collection.book_levels SET size = 12
            WHERE event_id IN (SELECT id FROM data_collection.normalized_events WHERE session_id = @session)
            """, id);
        var typedChanged = (await reader.ReadAsync(request, CancellationToken.None)).Value.Manifest;
        typedChanged.RawFingerprint.Should().Be(rawChanged.RawFingerprint);
        typedChanged.ProjectionFingerprint.Should().NotBe(rawChanged.ProjectionFingerprint);

        await ExecuteAsync(database.ConnectionString,
            "UPDATE data_collection.collector_session_tokens SET outcome = 'Higher' WHERE session_id = @session AND outcome_index = 0", id);
        var metadataChanged = (await reader.ReadAsync(request, CancellationToken.None)).Value.Manifest;
        metadataChanged.ManifestFingerprint.Should().NotBe(typedChanged.ManifestFingerprint);
        metadataChanged.ProjectionFingerprint.Should().Be(typedChanged.ProjectionFingerprint);
        var settingsChanged = (await reader.ReadAsync(request with
        {
            Inputs = new() { Settings = ImmutableSortedDictionary<string, string>.Empty.Add("experiment", "different") }
        }, CancellationToken.None)).Value.Manifest;
        settingsChanged.ManifestFingerprint.Should().NotBe(metadataChanged.ManifestFingerprint);
        var pinned = await reader.ReadAsync(request with { ExpectedManifestFingerprint = before.ManifestFingerprint }, CancellationToken.None);
        pinned.Error.Code.Should().Be("experiment.dataset.manifest_fingerprint_mismatch");
    }

    [Fact]
    public async Task ReadAsync_ShouldMatchIndependentPostgreSqlRawFingerprintIncludingMicrosecondsAndLf()
    {
        await using var database = await CreateDatabaseAsync();
        var id = Guid.NewGuid();
        await SeedAsync(database.ConnectionString, id);
        await SeedBooksAsync(database.ConnectionString, id);
        await ExecuteAsync(database.ConnectionString,
            "UPDATE data_collection.raw_market_messages SET received_at = received_at + interval '0.123456 seconds' WHERE session_id = @session", id);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT encode(sha256(convert_to(string_agg(
                id::text || ':' || connection_epoch::text || ':' ||
                to_char(received_at AT TIME ZONE 'UTC', 'YYYY-MM-DD HH24:MI:SS.US') || ':' ||
                encode(sha256(payload), 'hex'), E'\n' ORDER BY id), 'UTF8')), 'hex')
            FROM data_collection.raw_market_messages WHERE session_id = @session
            """;
        command.Parameters.AddWithValue("session", id);
        var expected = (string)(await command.ExecuteScalarAsync())!;
        await using var context = CreateContext(database.ConnectionString);

        var result = await CreateReader(context).ReadAsync(Request(id), CancellationToken.None);

        result.Value.Manifest.RawFingerprint.Should().Be(expected);
    }

    [Fact]
    public async Task ReadAsync_ShouldKeepOneSnapshotDuringConcurrentCommittedMutation()
    {
        await using var database = await CreateDatabaseAsync();
        var id = Guid.NewGuid();
        await SeedAsync(database.ConnectionString, id);
        await SeedBooksAsync(database.ConnectionString, id);
        await using var baselineContext = CreateContext(database.ConnectionString);
        var baseline = (await CreateReader(baselineContext).ReadAsync(Request(id), CancellationToken.None)).Value;
        var interceptor = new ConcurrentMutationInterceptor(async () =>
            await ExecuteAsync(database.ConnectionString, """
                BEGIN;
                UPDATE data_collection.raw_market_messages SET payload = payload || decode('20', 'hex') WHERE session_id = @session;
                UPDATE data_collection.book_levels SET size = 12
                  WHERE event_id IN (SELECT id FROM data_collection.normalized_events WHERE session_id = @session);
                UPDATE data_collection.collector_sessions SET market_slug = 'changed' WHERE id = @session;
                COMMIT;
                """, id));
        await using var context = new DataCollectionDbContext(new DbContextOptionsBuilder<DataCollectionDbContext>()
            .UseNpgsql(database.ConnectionString).AddInterceptors(interceptor).Options);

        var snapshot = await CreateReader(context).ReadAsync(Request(id), CancellationToken.None);
        var next = await CreateReader(context).ReadAsync(Request(id), CancellationToken.None);

        interceptor.Mutated.Should().BeTrue();
        snapshot.Value.Manifest.ManifestFingerprint.Should().Be(baseline.Manifest.ManifestFingerprint);
        snapshot.Value.ProjectionItems.Should().Equal(baseline.ProjectionItems);
        next.Value.Manifest.RawFingerprint.Should().NotBe(baseline.Manifest.RawFingerprint);
        next.Value.Manifest.ProjectionFingerprint.Should().NotBe(baseline.Manifest.ProjectionFingerprint);
        next.Value.Manifest.Session.MarketSlug.Should().Be("changed");
    }

    [Fact]
    public async Task ReadAsync_ShouldUseReadOnlyTransactionAndLeaveAllArchiveTablesUnchanged()
    {
        await using var database = await CreateDatabaseAsync();
        var id = Guid.NewGuid();
        await SeedAsync(database.ConnectionString, id);
        await SeedBooksAsync(database.ConnectionString, id);
        var before = await DatabaseFingerprintAsync(database.ConnectionString);
        var interceptor = new ReadOnlyInterceptor();
        await using var context = new DataCollectionDbContext(new DbContextOptionsBuilder<DataCollectionDbContext>()
            .UseNpgsql(database.ConnectionString).AddInterceptors(interceptor).Options);

        var result = await CreateReader(context).ReadAsync(Request(id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        interceptor.ReadOnly.Should().BeTrue();
        context.Database.CurrentTransaction.Should().BeNull();
        (await DatabaseFingerprintAsync(database.ConnectionString)).Should().Be(before);
    }

    [Fact]
    public async Task ReadAsync_WithUnknownMetadataAndDeclaredInputs_ShouldKeepIndependentDiagnostics()
    {
        await using var database = await CreateDatabaseAsync();
        var id = Guid.NewGuid();
        await SeedAsync(database.ConnectionString, id);
        await ExecuteAsync(database.ConnectionString,
            "UPDATE data_collection.collector_sessions SET event_starts_at = NULL, projection_version = NULL WHERE id = @session", id);
        await using var context = CreateContext(database.ConnectionString);
        var reference = new ExperimentDatasetReference("external-fixture", new string('a', 64));

        var result = await CreateReader(context).ReadAsync(Request(id) with
        {
            Inputs = new() { Btc = reference, FeeProfile = reference, MinimumOrderSizeProfile = reference }
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Suitability.MetadataComplete.Should().BeFalse();
        result.Value.Suitability.IsArchiveUsable.Should().BeFalse();
        result.Value.Suitability.Btc.Should().Be(DatasetKnowledge.Declared);
        result.Value.Suitability.Fees.Should().Be(DatasetKnowledge.Declared);
        result.Value.Suitability.MinimumOrderSize.Should().Be(DatasetKnowledge.Declared);
        result.Value.Manifest.Session.EventStartsAt.Should().BeNull();
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(4, 5)]
    [InlineData(3, 1)]
    public async Task ReadAsync_WithNonSuccessfulArchive_ShouldRejectWithoutChangingIt(int status, int reason)
    {
        await using var database = await CreateDatabaseAsync();
        var id = Guid.NewGuid();
        await SeedAsync(database.ConnectionString, id);
        await ExecuteAsync(database.ConnectionString,
            $"UPDATE data_collection.collector_sessions SET status = {status}, stop_reason = {reason} WHERE id = @session", id);
        await using var context = CreateContext(database.ConnectionString);

        var result = await CreateReader(context).ReadAsync(Request(id), CancellationToken.None);

        result.Error.Code.Should().Be("experiment.dataset.archive_not_successful");
    }

    [Fact]
    public async Task ReadAsync_WithMissingSession_ShouldReturnNotFound()
    {
        await using var database = await CreateDatabaseAsync();
        await using var context = CreateContext(database.ConnectionString);
        var result = await CreateReader(context).ReadAsync(Request(Guid.NewGuid()), CancellationToken.None);
        result.Error.Code.Should().Be("experiment.dataset.session_not_found");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadAsync_ShouldDistinguishMissingFeeScheduleFromExplicitZero(bool knownZero)
    {
        await using var database = await CreateDatabaseAsync();
        var id = Guid.NewGuid();
        await SeedAsync(database.ConnectionString, id);
        await SeedBooksAsync(database.ConnectionString, id);
        await SeedNewMarketAsync(database.ConnectionString, id);
        if (knownZero)
            await ExecuteAsync(database.ConnectionString, """
                UPDATE data_collection.new_markets SET fee_schedule_rate = 0, fee_schedule_exponent = 0,
                    fee_schedule_rebate_rate = 0, fee_schedule_taker_only = false
                WHERE event_id IN (SELECT id FROM data_collection.normalized_events WHERE session_id = @session)
                """, id);
        await using var context = CreateContext(database.ConnectionString);

        var result = await CreateReader(context).ReadAsync(Request(id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Suitability.IsArchiveUsable.Should().BeTrue();
        result.Value.Suitability.Fees.Should().Be(knownZero ? DatasetKnowledge.Available : DatasetKnowledge.Missing);
        result.Value.Suitability.MinimumOrderSize.Should().Be(DatasetKnowledge.Missing);
        result.Value.Manifest.Normalizers.Should().Contain(new ExperimentNormalizerVersion("new_market", 2));
        var item = result.Value.ProjectionItems.Single(item => item.EventType == "new_market");
        item.CanonicalContent.Should().Contain(knownZero ? "\"fee_schedule_rate\":0" : "\"fee_schedule_rate\":null");
    }

    [Fact]
    public async Task ReadAsync_ShouldNotUseForeignMarketAnnouncementAsFeeEvidence()
    {
        await using var database = await CreateDatabaseAsync();
        var id = Guid.NewGuid();
        await SeedAsync(database.ConnectionString, id);
        await SeedBooksAsync(database.ConnectionString, id);
        await SeedNewMarketAsync(database.ConnectionString, id);
        await ExecuteAsync(database.ConnectionString, """
            UPDATE data_collection.new_markets SET external_market_id = 'foreign', fee_schedule_rate = 0,
                fee_schedule_exponent = 0, fee_schedule_rebate_rate = 0, fee_schedule_taker_only = false
            WHERE event_id IN (SELECT id FROM data_collection.normalized_events WHERE session_id = @session);
            """, id);
        await using var context = CreateContext(database.ConnectionString);

        var result = await CreateReader(context).ReadAsync(Request(id), CancellationToken.None);

        result.Value.Suitability.IsArchiveUsable.Should().BeTrue();
        result.Value.Suitability.Fees.Should().Be(DatasetKnowledge.Missing);
    }

    [Fact]
    public async Task ReadAsync_WithMatchingMarketButDifferentOutcomeMetadata_ShouldBlockReplay()
    {
        await using var database = await CreateDatabaseAsync();
        var id = Guid.NewGuid();
        await SeedAsync(database.ConnectionString, id);
        await SeedBooksAsync(database.ConnectionString, id);
        await SeedNewMarketAsync(database.ConnectionString, id);
        await ExecuteAsync(database.ConnectionString, """
            UPDATE data_collection.new_market_assets SET outcome = 'Other'
            WHERE event_id IN (SELECT id FROM data_collection.normalized_events WHERE session_id = @session);
            """, id);
        await using var context = CreateContext(database.ConnectionString);

        var result = await CreateReader(context).ReadAsync(Request(id), CancellationToken.None);

        result.Value.Suitability.ProjectionConsistent.Should().BeFalse();
        result.Value.Suitability.IsArchiveUsable.Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_WithMalformedBookValues_ShouldDiagnoseProjectionBeforeMapping()
    {
        await using var database = await CreateDatabaseAsync();
        var id = Guid.NewGuid();
        await SeedAsync(database.ConnectionString, id);
        await SeedBooksAsync(database.ConnectionString, id);
        await ExecuteAsync(database.ConnectionString, """
            UPDATE data_collection.book_snapshots SET hash = ''
            WHERE event_id IN (SELECT id FROM data_collection.normalized_events WHERE session_id = @session);
            """, id);
        await using var context = CreateContext(database.ConnectionString);

        var result = await CreateReader(context).ReadAsync(Request(id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Suitability.IsArchiveUsable.Should().BeFalse();
        result.Value.Suitability.ProjectionConsistent.Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_ShouldKeepPriceChangeItemsAtomicOrderedAndImmutable()
    {
        await using var database = await CreateDatabaseAsync();
        var id = Guid.NewGuid();
        await SeedAsync(database.ConnectionString, id);
        await SeedBooksAsync(database.ConnectionString, id);
        await ExecuteAsync(database.ConnectionString, """
            WITH event AS (
                INSERT INTO data_collection.normalized_events
                    (raw_message_id, raw_item_index, projection_version, normalizer_version, event_type,
                     session_id, received_at, source_timestamp, market_condition_id, normalized_at)
                SELECT id, 1, 1, 1, 'price_change', session_id, received_at, 1792142399001,
                    'condition', received_at FROM data_collection.raw_market_messages
                WHERE id = (SELECT max(id) FROM data_collection.raw_market_messages WHERE session_id = @session)
                RETURNING id
            )
            INSERT INTO data_collection.price_change
                (event_id, item_index, asset_id, source_timestamp, price, size, side, hash)
            SELECT id, item_index, asset, 1792142399001, 0.40, 12, 1, 'changed'
            FROM event CROSS JOIN (VALUES (1, '1002'), (0, '1001')) AS changes(item_index, asset);
            """, id);
        await using var context = CreateContext(database.ConnectionString);

        var result = await CreateReader(context).ReadAsync(Request(id), CancellationToken.None);

        result.Value.Suitability.IsArchiveUsable.Should().BeTrue();
        var change = result.Value.OrderBookEvents.OfType<NormalizedOrderBookEvent.PriceChanges>().Single();
        change.Records.Select(item => item.AssetId).Should().Equal("1001", "1002");
        change.Records.Select(item => item.ItemIndex).Should().Equal(0, 1);
        var mutate = () => ((IList<PriceChangeRecord>)change.Records).Clear();
        mutate.Should().Throw<NotSupportedException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadAsync_ShouldRequireConsistentPersistedResolutionEvidence(bool corruptConfirmation)
    {
        await using var database = await CreateDatabaseAsync();
        var id = Guid.NewGuid();
        await SeedAsync(database.ConnectionString, id);
        await SeedBooksAsync(database.ConnectionString, id);
        await SeedResolutionAsync(database.ConnectionString, id);
        if (corruptConfirmation)
            await ExecuteAsync(database.ConnectionString, """
                UPDATE data_collection.resolution_observations SET winner_token_id = '1002', winner_outcome = 'Down'
                WHERE session_id = @session AND source = 2
                """, id);
        await using var context = CreateContext(database.ConnectionString);
        var logger = new CapturingLogger();

        var result = await CreateReader(context, logger).ReadAsync(Request(id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(logger.Exception?.ToString());
        result.Value.Suitability.Resolution.Should().Be(corruptConfirmation ? DatasetKnowledge.Conflicting : DatasetKnowledge.Available);
        result.Value.Suitability.Fees.Should().Be(DatasetKnowledge.Missing);
        result.Value.Suitability.Btc.Should().Be(DatasetKnowledge.Missing);
        result.Value.Manifest.Session.ResolutionConfirmedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ReadAsync_ShouldNotConfirmOutcomeWhenTypedSignalDisagreesWithObservations()
    {
        await using var database = await CreateDatabaseAsync();
        var id = Guid.NewGuid();
        await SeedAsync(database.ConnectionString, id);
        await SeedBooksAsync(database.ConnectionString, id);
        await SeedResolutionAsync(database.ConnectionString, id);
        await ExecuteAsync(database.ConnectionString, """
            UPDATE data_collection.market_resolutions SET winning_asset_id = '1002', winning_outcome = 'Down'
            WHERE event_id IN (SELECT id FROM data_collection.normalized_events WHERE session_id = @session)
            """, id);
        await using var context = CreateContext(database.ConnectionString);

        var result = await CreateReader(context).ReadAsync(Request(id), CancellationToken.None);

        result.Value.Suitability.Resolution.Should().Be(DatasetKnowledge.Conflicting);
    }

    [Fact]
    public async Task ReadAsync_WithCancellation_ShouldPropagateCancellationAndDisposeTransaction()
    {
        await using var database = await CreateDatabaseAsync();
        await using var context = CreateContext(database.ConnectionString);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var action = () => CreateReader(context).ReadAsync(Request(Guid.NewGuid()), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        context.Database.CurrentTransaction.Should().BeNull();
    }

    private static Task SeedNewMarketAsync(string connectionString, Guid sessionId) =>
        ExecuteAsync(connectionString, """
            WITH event AS (
                INSERT INTO data_collection.normalized_events
                    (raw_message_id, raw_item_index, projection_version, normalizer_version, event_type,
                     session_id, received_at, market_condition_id, normalized_at)
                SELECT id, 2, 1, 2, 'new_market', session_id, received_at, 'condition', received_at
                FROM data_collection.raw_market_messages
                WHERE id = (SELECT min(id) FROM data_collection.raw_market_messages WHERE session_id = @session)
                RETURNING id
            ), market AS (
                INSERT INTO data_collection.new_markets
                    (event_id, external_market_id, question, slug, description, active, sports_market_type,
                     game_start_time, order_price_min_tick_size, group_item_title, taker_base_fee, fees_enabled,
                     event_message_id, event_message_ticker, event_message_slug, event_message_title, event_message_description)
                SELECT id, 'market', 'Question', 'market-slug', 'Description', true, '', '', 0.01, '', 0, true,
                       'event', 'ticker', 'event-slug', 'Title', 'Description' FROM event
            )
            INSERT INTO data_collection.new_market_assets (event_id, item_index, asset_id, outcome)
            SELECT id, item_index, asset, outcome FROM event
            CROSS JOIN (VALUES (0, '1001', 'Up'), (1, '1002', 'Down')) AS assets(item_index, asset, outcome);
            """, sessionId);

    private static Task SeedResolutionAsync(string connectionString, Guid sessionId) =>
        ExecuteAsync(connectionString, """
            WITH event AS (
                INSERT INTO data_collection.normalized_events
                    (raw_message_id, raw_item_index, projection_version, normalizer_version, event_type,
                     session_id, received_at, market_condition_id, normalized_at)
                SELECT id, 3, 1, 1, 'market_resolved', session_id, received_at, 'condition', received_at
                FROM data_collection.raw_market_messages
                WHERE id = (SELECT min(id) FROM data_collection.raw_market_messages WHERE session_id = @session)
                RETURNING id, raw_message_id
            ), resolution AS (
                INSERT INTO data_collection.market_resolutions (event_id, external_market_id, winning_asset_id, winning_outcome)
                SELECT id, 'market', '1001', 'Up' FROM event
            ), assets AS (
                INSERT INTO data_collection.market_resolution_assets (event_id, item_index, asset_id)
                SELECT id, item_index, asset FROM event CROSS JOIN (VALUES (0, '1001'), (1, '1002')) AS assets(item_index, asset)
            )
            INSERT INTO data_collection.resolution_observations
                (session_id, source, observed_at, status, winner_token_id, winner_outcome,
                 external_market_id, condition_id, raw_message_id, raw_item_index, connection_epoch)
            SELECT @session, 0, '2026-10-16 09:19:59Z', 2, '1001', 'Up', 'market', 'condition', raw_message_id, 3, 1 FROM event;
            INSERT INTO data_collection.resolution_observations
                (session_id, source, observed_at, status, winner_token_id, winner_outcome,
                 external_event_id, event_slug, external_market_id, market_slug, condition_id, closed, accepting_orders)
            VALUES (@session, 1, '2026-10-16 09:25:02Z', 2, '1001', 'Up', 'event', 'event-slug', 'market', 'market-slug', 'condition', true, false),
                   (@session, 2, '2026-10-16 09:25:03Z', 2, '1001', 'Up', NULL, NULL, NULL, NULL, 'condition', true, false);
            INSERT INTO data_collection.resolution_observation_outcomes
                (observation_id, outcome_index, token_id, outcome, price, is_winner)
            SELECT id, outcome_index, token, outcome, price, winner FROM data_collection.resolution_observations
            CROSS JOIN (VALUES (0, '1001', 'Up', 1.00, true), (1, '1002', 'Down', 0.00, false))
                AS outcomes(outcome_index, token, outcome, price, winner)
            WHERE session_id = @session;
            INSERT INTO data_collection.resolution_states
                (session_id, last_scanned_raw_message_id, primary_observation_id, confirming_observation_id, confirmed_at)
            SELECT @session, 0, min(id) FILTER (WHERE source = 1), min(id) FILTER (WHERE source = 2), '2026-10-16 09:25:04Z'
            FROM data_collection.resolution_observations WHERE session_id = @session;
            UPDATE data_collection.collector_sessions SET winning_token_id = '1001', winning_outcome = 'Up',
                resolution_signaled_at = '2026-10-16 09:19:59Z', resolution_connection_epoch = 1,
                resolution_confirmed_at = '2026-10-16 09:25:04Z' WHERE id = @session;
            """, sessionId);

    private async Task<PostgreSqlTestDatabase> CreateDatabaseAsync()
    {
        var database = await fixture.CreateDatabaseAsync();
        await using var context = CreateContext(database.ConnectionString);
        await context.Database.MigrateAsync();
        return database;
    }

    private static ExperimentDatasetRequest Request(Guid id) => new(CollectorSessionId.Create(id).Value, 1);

    private static async Task ExecuteAsync(string connectionString, string sql, Guid sessionId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("session", sessionId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedBooksAsync(string connectionString, Guid sessionId) =>
        await ExecuteAsync(connectionString, """
            WITH raw AS (
                INSERT INTO data_collection.raw_market_messages (session_id, connection_epoch, received_at, payload)
                VALUES (@session, 1, '2026-10-16 09:19:59.001Z', convert_to('{"event_type":"book","asset_id":"1001"}', 'UTF8')),
                       (@session, 1, '2026-10-16 09:19:59.002Z', convert_to('{"event_type":"book","asset_id":"1002"}', 'UTF8'))
                RETURNING id, session_id, received_at, payload
            ), ledger AS (
                INSERT INTO data_collection.raw_message_normalizations
                    (raw_message_id, projection_version, status, attempt_count, completed_at)
                SELECT id, 1, 3, 1, '2026-10-16 09:25:00Z' FROM raw
            ), events AS (
                INSERT INTO data_collection.normalized_events
                    (raw_message_id, raw_item_index, projection_version, normalizer_version, event_type,
                     session_id, received_at, source_timestamp, market_condition_id, asset_id, normalized_at)
                SELECT id, 0, 1, 1, 'book', session_id, received_at, 1792142399000, 'condition',
                       convert_from(payload, 'UTF8')::jsonb ->> 'asset_id', '2026-10-16 09:25:00Z'
                FROM raw RETURNING id
            ), snapshots AS (
                INSERT INTO data_collection.book_snapshots (event_id, hash, tick_size)
                SELECT id, 'book-hash', 0.01 FROM events
            )
            INSERT INTO data_collection.book_levels (event_id, side, level_index, price, size)
            SELECT id, side, 0, price, 10 FROM events
            CROSS JOIN (VALUES (1, 0.40), (2, 0.60)) AS levels(side, price);
            """, sessionId);

    private static async Task<string> DatabaseFingerprintAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var tables = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT tablename FROM pg_tables WHERE schemaname = 'data_collection' ORDER BY tablename";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        }
        var content = new StringBuilder();
        foreach (var table in tables)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COALESCE(string_agg(row_json, E'\\n' ORDER BY row_json), '') FROM (SELECT to_jsonb(row)::text AS row_json FROM data_collection.\"{table}\" AS row) AS rows";
            content.Append(table).Append(await command.ExecuteScalarAsync());
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.ToString())));
    }

    private sealed class ConcurrentMutationInterceptor(Func<Task> mutate) : DbCommandInterceptor
    {
        public bool Mutated { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (!Mutated && command.CommandText.Contains("collector_sessions", StringComparison.Ordinal))
            {
                Mutated = true;
                await mutate();
            }
            return result;
        }
    }

    private sealed class ReadOnlyInterceptor : DbCommandInterceptor
    {
        public bool ReadOnly { get; private set; }

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SET TRANSACTION READ ONLY", StringComparison.Ordinal))
            {
                await using var check = command.Connection!.CreateCommand();
                check.Transaction = command.Transaction;
                check.CommandText = "SHOW transaction_read_only";
                ReadOnly = (string)(await check.ExecuteScalarAsync(cancellationToken))! == "on";
            }
            return result;
        }
    }

    private static DataCollectionDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<DataCollectionDbContext>().UseNpgsql(connectionString).Options);

    private static ExperimentDatasetReader CreateReader(DataCollectionDbContext context, ILogger<ExperimentDatasetReader>? logger = null) =>
        new(context, new CollectorSessionRepository(context), new NormalizationSuitabilityReader(context),
            new OrderBookIntegrityReader(context),
            new CollectorOrderBookIntegrityCoordinator(new OrderBookIntegrityReader(context),
                new OrderBookProjector(), NullLogger<CollectorOrderBookIntegrityCoordinator>.Instance),
            logger ?? NullLogger<ExperimentDatasetReader>.Instance);

    private sealed class CapturingLogger : ILogger<ExperimentDatasetReader>
    {
        public Exception? Exception { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Exception = exception;
    }

    private static async Task SeedAsync(string connectionString, Guid sessionId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO data_collection.collector_sessions
                (id, market_id, status, stop_reason, created_at, external_event_id, event_slug,
                 external_market_id, market_slug, condition_id, event_starts_at, event_ends_at,
                 projection_version, stopped_at)
            VALUES (@session, @market, 3, 3, '2026-10-16 09:19:00Z', 'event', 'event-slug',
                    'market', 'market-slug', 'condition', '2026-10-16 09:20:00Z',
                    '2026-10-16 09:25:00Z', 1, '2026-10-16 09:25:01Z');
            INSERT INTO data_collection.collector_session_tokens
                (session_id, token_id, outcome, outcome_index)
            VALUES (@session, '1001', 'Up', 0), (@session, '1002', 'Down', 1);
            WITH raw AS (
                INSERT INTO data_collection.raw_market_messages
                    (session_id, connection_epoch, received_at, payload)
                VALUES (@session, 1, '2026-10-16 09:19:59Z', decode('5b5d', 'hex'))
                RETURNING id
            )
            INSERT INTO data_collection.raw_message_normalizations
                (raw_message_id, projection_version, status, attempt_count, completed_at)
            SELECT id, 1, 3, 1, '2026-10-16 09:25:00Z' FROM raw;
            """;
        command.Parameters.AddWithValue("session", sessionId);
        command.Parameters.AddWithValue("market", Guid.NewGuid());
        await command.ExecuteNonQueryAsync();
    }
}
