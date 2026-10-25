using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;
using PolymarketLab.DataCollection.Core.Application.Simulation.Datasets;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorOrderBookIntegrity;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.SharedKernel.Errors;
using Session = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;
using Fingerprints = PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Simulation.ExperimentDatasetFingerprintBuilder;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Simulation;

/// <summary>Материализует архив одной успешной сессии в общей транзакции RepeatableRead READ ONLY.</summary>
public sealed class ExperimentDatasetReader(
    DataCollectionDbContext dbContext,
    ICollectorSessionRepository sessionRepository,
    INormalizationSuitabilityReader suitabilityReader,
    IOrderBookIntegrityReader orderBookReader,
    ICollectorOrderBookIntegrityCoordinator integrityCoordinator,
    ILogger<ExperimentDatasetReader> logger) : IExperimentDatasetReader
{
    /// <inheritdoc />
    public async Task<Result<ExperimentDatasetSnapshot, Error>> ReadAsync(
        ExperimentDatasetRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SessionId is null || request.ProjectionVersion <= 0 || !ValidInputs(request))
            return Failure("invalid_request", "Dataset selection, references or fingerprints are invalid.", ErrorType.Validation);
        if (dbContext.Database.CurrentTransaction is not null)
            return Failure("transaction_exists", "Dataset reader must own its read-only snapshot transaction.");

        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.RepeatableRead, cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "SET TRANSACTION READ ONLY; SET LOCAL TIME ZONE 'UTC'", cancellationToken);

            var session = await sessionRepository.GetByIdAsync(request.SessionId, cancellationToken);
            if (session is null)
                return Failure("session_not_found", "Selected collector session was not found.", ErrorType.NotFound);
            if (session.ProjectionVersion is not null && session.ProjectionVersion != request.ProjectionVersion)
                return Failure("projection_version_mismatch", "Requested projection version differs from the session snapshot.");
            if (session.Status != CollectorSessionStatus.Stopped
                || session.StopReason != CollectorStopReason.MarketClosed
                || session.InvalidatingAt is not null || session.FailureCode is not null)
                return Failure("archive_not_successful", "Only successfully retained Stopped/MarketClosed archives are supported.");

            var diagnostics = ImmutableArray.CreateBuilder<ExperimentDatasetDiagnostic>();
            var metadataComplete = HasMetadata(session);
            if (!metadataComplete)
                Add("metadata_unknown", "Required immutable session metadata is missing or invalid.");

            var normalization = await suitabilityReader.ReadAsync(
                request.SessionId, request.ProjectionVersion, cancellationToken);
            var normalizationComplete = normalization.RawCount > 0
                && normalization.LedgerCount == normalization.RawCount
                && normalization.ProcessedCount == normalization.RawCount;
            var raw = await ReadRawAsync(request, cancellationToken);
            var ledgerComplete = true;
            using var ledgerHash = new Fingerprints.Lines();
            await using (var command = Command(ExperimentDatasetQueries.Ledger, request))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    using var json = JsonDocument.Parse(reader.GetString(0));
                    ledgerHash.Append(Fingerprints.Canonicalize(json.RootElement));
                    if (json.RootElement.GetProperty("status").GetInt32() != 3
                        || json.RootElement.GetProperty("completed_at").ValueKind == JsonValueKind.Null)
                        ledgerComplete = false;
                }
            }
            normalizationComplete &= ledgerComplete;
            if (!normalizationComplete)
                Add("normalization_incomplete", $"Raw={normalization.RawCount}, Processed={normalization.ProcessedCount}, Missing={normalization.MissingCount}.");

            var projection = ImmutableArray.CreateBuilder<ExperimentProjectionItem>();
            var versions = new HashSet<(string EventType, int Version)>();
            var projectionConsistent = true;
            using var projectionHash = new Fingerprints.Lines();
            var feeEvidence = new HashSet<string>(StringComparer.Ordinal);
            await using (var command = Command(ExperimentDatasetQueries.Projection, request))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    var rawId = reader.GetInt64(0);
                    var itemIndex = reader.GetInt32(1);
                    var eventType = reader.GetString(2);
                    var version = reader.GetInt32(3);
                    using var json = JsonDocument.Parse(reader.GetString(6));
                    var root = json.RootElement;
                    var content = Fingerprints.Canonicalize(root);
                    projectionHash.Append(content);
                    projection.Add(new(rawId, itemIndex, eventType, content));
                    versions.Add((eventType, version));
                    if (!reader.GetBoolean(4) || reader.IsDBNull(5) || !reader.GetBoolean(5)
                        || !ValidProjection(root, eventType, version, session))
                    {
                        projectionConsistent = false;
                        // Ограничиваем размер отчёта, не теряя факт нарушения.
                        if (!diagnostics.Any(item => item.Code == "experiment.dataset.projection_inconsistent"))
                            Add("projection_inconsistent", $"Invalid projection at raw {rawId}, item {itemIndex}.");
                    }
                    ReadFeeEvidence(root, eventType, session, feeEvidence);
                }
            }

            string evidence;
            await using (var command = Command(ExperimentDatasetQueries.Evidence, request))
            {
                var value = (string)(await command.ExecuteScalarAsync(cancellationToken))!;
                using var json = JsonDocument.Parse(value);
                evidence = Fingerprints.Canonicalize(json.RootElement);
            }
            var resolution = ExperimentDatasetEvidence.ReadResolution(
                evidence, session, normalization.ResolutionRawItemProcessed, projection);
            var fees = feeEvidence.Count switch
            {
                > 1 => DatasetKnowledge.Conflicting,
                1 => DatasetKnowledge.Available,
                _ => request.Inputs.FeeProfile is null ? DatasetKnowledge.Missing : DatasetKnowledge.Declared
            };
            var minimum = request.Inputs.MinimumOrderSizeProfile is null ? DatasetKnowledge.Missing : DatasetKnowledge.Declared;
            var btc = request.Inputs.Btc is null ? DatasetKnowledge.Missing : DatasetKnowledge.Declared;
            NoteKnowledge("resolution", resolution);
            NoteKnowledge("fees", fees);
            NoteKnowledge("minimum_order_size", minimum);
            NoteKnowledge("btc", btc);

            ImmutableArray<NormalizedOrderBookEvent> events = [];
            bool? integrity = null;
            if (metadataComplete && normalizationComplete && projectionConsistent)
            {
                try
                {
                    events = (await orderBookReader.ReadAsync(request.SessionId, request.ProjectionVersion, cancellationToken)).ToImmutableArray();
                }
                catch (ArgumentException)
                {
                    // Конструкторы существующих моделей остаются источником проверки значений.
                    // Не дублируем их финансовые инварианты новым нормализатором.
                    projectionConsistent = false;
                    Add("projection_invalid_values", "Typed projection violates the existing order book model invariants.");
                }
                if (projectionConsistent)
                {
                    var result = await integrityCoordinator.EvaluateAsync(
                        request.SessionId, request.ProjectionVersion,
                        session.Tokens.Select(token => token.TokenId).ToArray(),
                        session.EventStartsAt, session.EventEndsAt, cancellationToken);
                    integrity = result.IsSuccess;
                    if (result.IsFailure)
                        diagnostics.Add(new(result.Error.Code, result.Error.Message, true));
                }
            }

            var inputs = request.Inputs with
            {
                Settings = request.Inputs.Settings.ToImmutableSortedDictionary(StringComparer.Ordinal)
            };
            var manifest = new ExperimentDatasetManifest(
                1, Snapshot(session), request.ProjectionVersion, raw.Count, raw.First, raw.Last,
                raw.Hash, ledgerHash.Finish(), projectionHash.Finish(), Fingerprints.Hash(evidence),
                new Dictionary<string, string>
                {
                    ["raw"] = "sha256-lf-utc-microseconds-v1",
                    ["canonicalJson"] = "ordinal-properties-decimal-g29-v1",
                    ["projection"] = "typed-archive-rows-v1",
                    ["terminalIntegrity"] = "collector-order-book-integrity-v1"
                }.ToImmutableSortedDictionary(StringComparer.Ordinal),
                versions.OrderBy(value => value.EventType, StringComparer.Ordinal).ThenBy(value => value.Version)
                    .Select(value => new ExperimentNormalizerVersion(value.EventType, value.Version)).ToImmutableArray(),
                inputs, string.Empty);
            // Поле собственного отпечатка исключено, а не заполнено текущим временем или случайным ID.
            var manifestJson = JsonSerializer.SerializeToElement(manifest);
            var fields = manifestJson.EnumerateObject()
                .Where(property => property.Name != nameof(manifest.ManifestFingerprint))
                .ToDictionary(property => property.Name, property => property.Value);
            manifest = manifest with { ManifestFingerprint = Fingerprints.Hash(Fingerprints.Canonicalize(fields)) };
            if (request.ExpectedRawFingerprint is not null
                && !string.Equals(request.ExpectedRawFingerprint, manifest.RawFingerprint, StringComparison.OrdinalIgnoreCase))
                return Failure("raw_fingerprint_mismatch", "Raw input differs from the pinned fingerprint.");
            if (request.ExpectedManifestFingerprint is not null
                && !string.Equals(request.ExpectedManifestFingerprint, manifest.ManifestFingerprint, StringComparison.OrdinalIgnoreCase))
                return Failure("manifest_fingerprint_mismatch", "Dataset input differs from the pinned manifest.");

            return new ExperimentDatasetSnapshot(manifest,
                new(metadataComplete, normalizationComplete, projectionConsistent, integrity,
                    resolution, fees, minimum, btc, diagnostics.ToImmutable()),
                normalization, events, projection.ToImmutable(), evidence);

            void Add(string code, string message) => diagnostics.Add(new($"experiment.dataset.{code}", message, true));
            void NoteKnowledge(string name, DatasetKnowledge knowledge)
            {
                if (knowledge != DatasetKnowledge.Available)
                    diagnostics.Add(new($"experiment.dataset.{name}.{knowledge.ToString().ToLowerInvariant()}",
                        $"{name}: {knowledge}.", false));
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to read experiment dataset for session {SessionId}.", request.SessionId.Value);
            var code = exception is PostgresException postgres ? $"read_failed.{postgres.SqlState}" : "read_failed";
            return Failure(code, "Could not read the selected experiment dataset.", ErrorType.Failure);
        }
    }

    private async Task<(long Count, long? First, long? Last, string Hash)> ReadRawAsync(
        ExperimentDatasetRequest request, CancellationToken cancellationToken)
    {
        await using var command = Command(ExperimentDatasetQueries.Raw, request);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        using var hash = new Fingerprints.Lines();
        long count = 0;
        long? first = null, last = null;
        while (await reader.ReadAsync(cancellationToken))
        {
            first ??= reader.GetInt64(0);
            last = reader.GetInt64(0);
            count++;
            hash.Append(reader.GetString(1));
        }
        return (count, first, last, hash.Finish());
    }

    private DbCommand Command(string sql, ExperimentDatasetRequest request)
    {
        var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = sql;
        var session = command.CreateParameter();
        session.ParameterName = "session_id";
        session.Value = request.SessionId.Value;
        command.Parameters.Add(session);
        var version = command.CreateParameter();
        version.ParameterName = "projection_version";
        version.Value = request.ProjectionVersion;
        command.Parameters.Add(version);
        return command;
    }

    private static bool ValidInputs(ExperimentDatasetRequest request) => request.Inputs is not null
        && request.Inputs.Settings is not null
        && request.Inputs.Settings.All(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null)
        && new[] { request.Inputs.Btc, request.Inputs.FeeProfile, request.Inputs.MinimumOrderSizeProfile }
            .All(reference => reference is null || !string.IsNullOrWhiteSpace(reference.Name) && IsHash(reference.Fingerprint))
        && (request.ExpectedRawFingerprint is null || IsHash(request.ExpectedRawFingerprint))
        && (request.ExpectedManifestFingerprint is null || IsHash(request.ExpectedManifestFingerprint));

    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool HasMetadata(Session session) =>
        new[] { session.ExternalEventId, session.EventSlug, session.ExternalMarketId, session.MarketSlug, session.ConditionId }
            .All(value => !string.IsNullOrWhiteSpace(value))
        && session.ProjectionVersion > 0
        && session.EventStartsAt.HasValue && session.EventEndsAt > session.EventStartsAt
        && session.Tokens.Count >= 2
        && session.Tokens.Select(token => token.TokenId.Value).Distinct(StringComparer.Ordinal).Count() == session.Tokens.Count
        && session.Tokens.All(token => token.OutcomeIndex >= 0 && !string.IsNullOrWhiteSpace(token.Outcome));

    private static ExperimentSessionSnapshot Snapshot(Session session) => new(
        session.Id.Value, session.MarketId.Value, session.ExternalEventId, session.EventSlug,
        session.ExternalMarketId, session.MarketSlug, session.ConditionId,
        session.EventStartsAt?.ToUniversalTime(), session.EventEndsAt?.ToUniversalTime(),
        session.Tokens.Select(token => new ExperimentSessionToken(token.TokenId.Value, token.Outcome, token.OutcomeIndex)).ToImmutableArray(),
        session.ResolutionConfirmedAt?.ToUniversalTime(), session.WinningTokenId, session.WinningOutcome);

    private static bool ValidProjection(JsonElement root, string eventType, int version, Session session)
    {
        if (version <= 0 || version > (eventType == "new_market" ? 2 : 1))
            return false;
        var required = eventType switch
        {
            "book" => "book_snapshots", "price_change" => "price_change",
            "tick_size_change" => "tick_size_changes", "best_bid_ask" => "best_bid_asks",
            "last_trade_price" => "last_trade_price", "new_market" => "new_markets",
            "market_resolved" => "market_resolutions", _ => null
        };
        if (required is null || root.GetProperty(required).GetArrayLength() == 0
            || required != "price_change" && root.GetProperty(required).GetArrayLength() != 1)
            return false;
        if (ExperimentDatasetQueries.TypedTables.Any(table => table.EventType != eventType && root.GetProperty(table.Table).GetArrayLength() != 0))
            return false;
        if (eventType == "new_market")
            return root.GetProperty("new_market_assets").GetArrayLength() >= 2
                && (root.GetProperty("new_markets")[0].GetProperty("external_market_id").GetString() != session.ExternalMarketId
                    || MatchesMarketIdentity(root, session));

        var header = root.GetProperty("header");
        if (header.GetProperty("market_condition_id").GetString() != session.ConditionId)
            return false;
        var tokens = session.Tokens.Select(token => token.TokenId.Value).ToHashSet(StringComparer.Ordinal);
        if (eventType == "price_change")
            return root.GetProperty("price_change").EnumerateArray().All(item => tokens.Contains(item.GetProperty("asset_id").GetString()!));
        if (eventType == "market_resolved")
        {
            var resolution = root.GetProperty("market_resolutions")[0];
            var assets = root.GetProperty("market_resolution_assets").EnumerateArray().ToArray();
            return resolution.GetProperty("external_market_id").GetString() == session.ExternalMarketId
                && session.Tokens.Any(token => token.TokenId.Value == resolution.GetProperty("winning_asset_id").GetString()
                    && token.Outcome == resolution.GetProperty("winning_outcome").GetString())
                && assets.Length == tokens.Count
                && assets.Select(item => item.GetProperty("asset_id").GetString()).ToHashSet().SetEquals(tokens);
        }
        return tokens.Contains(header.GetProperty("asset_id").GetString()!);
    }

    private static void ReadFeeEvidence(JsonElement root, string eventType, Session session, HashSet<string> fees)
    {
        if (eventType != "new_market") return;
        foreach (var market in root.GetProperty("new_markets").EnumerateArray())
        {
            if (!MatchesMarketIdentity(root, session)) continue;
            if (market.GetProperty("fee_schedule_rate").ValueKind == JsonValueKind.Null
                || market.GetProperty("fee_schedule_exponent").ValueKind == JsonValueKind.Null
                || market.GetProperty("fee_schedule_rebate_rate").ValueKind == JsonValueKind.Null
                || market.GetProperty("fee_schedule_taker_only").ValueKind == JsonValueKind.Null) continue;
            fees.Add(Fingerprints.Canonicalize(market.EnumerateObject()
                .Where(property => property.Name.StartsWith("fee", StringComparison.Ordinal) || property.Name == "taker_base_fee")
                .ToDictionary(property => property.Name, property => property.Value)));
        }
    }

    private static bool MatchesMarketIdentity(JsonElement root, Session session)
    {
        var market = root.GetProperty("new_markets")[0];
        var assets = root.GetProperty("new_market_assets").EnumerateArray().ToArray();
        return market.GetProperty("external_market_id").GetString() == session.ExternalMarketId
            && market.GetProperty("slug").GetString() == session.MarketSlug
            && market.GetProperty("event_message_id").GetString() == session.ExternalEventId
            && market.GetProperty("event_message_slug").GetString() == session.EventSlug
            && root.GetProperty("header").GetProperty("market_condition_id").GetString() == session.ConditionId
            && assets.Length == session.Tokens.Count
            && assets.Zip(session.Tokens).All(pair =>
                pair.First.GetProperty("asset_id").GetString() == pair.Second.TokenId.Value
                && pair.First.GetProperty("outcome").GetString() == pair.Second.Outcome
                && pair.First.GetProperty("item_index").GetInt32() == pair.Second.OutcomeIndex);
    }

    private static Error Failure(string code, string message, ErrorType type = ErrorType.Conflict) =>
        new($"experiment.dataset.{code}", message, type);
}
