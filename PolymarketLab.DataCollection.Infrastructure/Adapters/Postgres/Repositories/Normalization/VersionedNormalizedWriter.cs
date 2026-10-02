using System.Data.Common;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.CollectorSession;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Normalization;

internal sealed class VersionedNormalizedWriter(
    DataCollectionDbContext dbContext,
    TimeProvider timeProvider) : INormalizedMessageWriter
{
    private const int MaximumErrorCodeLength = 200;
    private const int MaximumErrorMessageLength = 2000;
    private const int MaximumErrorFieldLength = 500;

    public async Task<IReadOnlyList<NormalizationWriteStatus>> WriteBatchAsync(
        IReadOnlyList<NormalizationWriteRequest> requests,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0)
            return [];
        if (requests.Count > INormalizedMessageWriter.MaximumBatchSize)
            throw new ArgumentOutOfRangeException(nameof(requests));

        foreach (var request in requests)
            Validate(request.Claim, request.Completion);

        var duplicate = requests
            .GroupBy(request => (
                request.Claim.Message.RawMessageId,
                request.Claim.ProjectionVersion))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException("A normalization write batch cannot contain duplicate claims.", nameof(requests));

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        try
        {
            var statuses = new NormalizationWriteStatus[requests.Count];
            var fencedSessions = await CollectorSessionWriteFence.LockAsync(
                dbContext,
                transaction,
                requests.Select(request => request.Claim.Message.SessionId),
                cancellationToken);
            var writable = new List<(int Index, NormalizationWriteRequest Request)>();
            var ledgers = await LockLedgersAsync(requests, transaction, cancellationToken);
            foreach (var item in requests
                         .Select((request, index) => (Index: index, Request: request))
                         .OrderBy(item => item.Request.Claim.Message.RawMessageId)
                         .ThenBy(item => item.Request.Claim.ProjectionVersion))
            {
                if (fencedSessions.Contains(item.Request.Claim.Message.SessionId))
                {
                    statuses[item.Index] = NormalizationWriteStatus.ClaimLost;
                    continue;
                }

                var ledgerFound = ledgers.TryGetValue(
                    (item.Request.Claim.Message.RawMessageId, item.Request.Claim.ProjectionVersion),
                    out var ledger);
                if (!ledgerFound
                    || ledger.Status != NormalizationStatus.Processing
                    || ledger.AttemptCount != item.Request.Claim.AttemptCount)
                {
                    statuses[item.Index] = ledgerFound && IsTerminal(ledger.Status)
                        ? NormalizationWriteStatus.AlreadyCompleted
                        : NormalizationWriteStatus.ClaimLost;
                    continue;
                }

                writable.Add(item);
            }

            var normalizedAt = timeProvider.GetUtcNow();
            var events = writable
                .SelectMany(item => item.Request.Completion.Events)
                .Select(normalizedEvent => new EventWrite(
                    normalizedEvent,
                    new NormalizedEventRecord(normalizedEvent, normalizedAt)))
                .ToArray();
            dbContext.NormalizedEvents.AddRange(events.Select(item => item.Entity));
            await dbContext.SaveChangesAsync(cancellationToken);

            foreach (var item in events)
                AddTypedRows(item.Event, item.Entity.Id);

            await dbContext.SaveChangesAsync(cancellationToken);
            var completed = await CompleteLedgersAsync(writable, transaction, cancellationToken);
            if (completed.Count != writable.Count)
            {
                throw new InvalidOperationException(
                    "A normalization claim changed while its batch was being written.");
            }

            foreach (var item in writable)
                statuses[item.Index] = NormalizationWriteStatus.Written;

            await transaction.CommitAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return statuses;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<NormalizationWriteStatus> WriteAsync(
        ClaimedRawMessage claim,
        NormalizationCompletion completion,
        CancellationToken cancellationToken)
    {
        var statuses = await WriteBatchAsync(
            [new NormalizationWriteRequest(claim, completion)],
            cancellationToken);
        return statuses[0];
    }

    private async Task<IReadOnlyDictionary<(long RawMessageId, int ProjectionVersion), LedgerState>>
        LockLedgersAsync(
        IReadOnlyList<NormalizationWriteRequest> requests,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            """
            SELECT normalization.raw_message_id,
                   normalization.projection_version,
                   normalization.status,
                   normalization.attempt_count
            FROM data_collection.raw_message_normalizations AS normalization
            INNER JOIN unnest(@raw_message_ids::bigint[], @projection_versions::integer[])
                AS requested(raw_message_id, projection_version)
              ON requested.raw_message_id = normalization.raw_message_id
             AND requested.projection_version = normalization.projection_version
            ORDER BY normalization.raw_message_id, normalization.projection_version
            FOR UPDATE OF normalization
            """;
        AddParameter(
            command,
            "raw_message_ids",
            requests.Select(request => request.Claim.Message.RawMessageId).ToArray());
        AddParameter(
            command,
            "projection_versions",
            requests.Select(request => request.Claim.ProjectionVersion).ToArray());
        var ledgers = new Dictionary<(long, int), LedgerState>(requests.Count);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ledgers.Add(
                (reader.GetInt64(0), reader.GetInt32(1)),
                new LedgerState(
                    (NormalizationStatus)reader.GetInt32(2),
                    reader.GetInt32(3)));
        }

        return ledgers;
    }

    private async Task<IReadOnlySet<(long RawMessageId, int ProjectionVersion)>> CompleteLedgersAsync(
        IReadOnlyList<(int Index, NormalizationWriteRequest Request)> writable,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (writable.Count == 0)
            return new HashSet<(long, int)>();

        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        var values = new StringBuilder();
        for (var index = 0; index < writable.Count; index++)
        {
            if (index > 0)
                values.AppendLine(",");

            values.Append($"""
                (CAST(@raw_message_id_{index} AS bigint),
                 CAST(@projection_version_{index} AS integer),
                 CAST(@status_{index} AS integer),
                 CAST(@attempt_count_{index} AS integer),
                 CAST(@error_code_{index} AS text),
                 CAST(@error_message_{index} AS text),
                 CAST(@error_field_{index} AS text))
                """);
            var request = writable[index].Request;
            AddParameter(command, $"raw_message_id_{index}", request.Claim.Message.RawMessageId);
            AddParameter(command, $"projection_version_{index}", request.Claim.ProjectionVersion);
            AddParameter(command, $"status_{index}", (int)request.Completion.Status);
            AddParameter(command, $"attempt_count_{index}", request.Claim.AttemptCount);
            AddParameter(
                command,
                $"error_code_{index}",
                (object?)request.Completion.Issue?.Code ?? DBNull.Value);
            AddParameter(
                command,
                $"error_message_{index}",
                (object?)request.Completion.Issue?.Message ?? DBNull.Value);
            AddParameter(
                command,
                $"error_field_{index}",
                (object?)request.Completion.Issue?.Field ?? DBNull.Value);
        }

        command.CommandText = $"""
            UPDATE data_collection.raw_message_normalizations AS normalization
            SET status = completion.status,
                completed_at = CURRENT_TIMESTAMP,
                error_code = completion.error_code,
                error_message = completion.error_message,
                error_field = completion.error_field
            FROM (VALUES
            {values}
            ) AS completion(
                raw_message_id,
                projection_version,
                status,
                attempt_count,
                error_code,
                error_message,
                error_field)
            WHERE normalization.raw_message_id = completion.raw_message_id
              AND normalization.projection_version = completion.projection_version
              AND normalization.status = {(int)NormalizationStatus.Processing}
              AND normalization.attempt_count = completion.attempt_count
            RETURNING normalization.raw_message_id, normalization.projection_version
            """;

        var completed = new HashSet<(long, int)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            completed.Add((reader.GetInt64(0), reader.GetInt32(1)));

        return completed;
    }

    private void AddTypedRows(NormalizedEvent normalizedEvent, long eventId)
    {
        switch (normalizedEvent.EventType)
        {
            case "last_trade_price":
                dbContext.LastTradePrices.Add(new LastTradePriceEntity(
                    eventId,
                    (LastTradeRecord)normalizedEvent.Records[0]));
                break;
            case "price_change":
                dbContext.PriceChanges.AddRange(normalizedEvent.Records
                    .Cast<PriceChangeRecord>()
                    .Select(record => new PriceChangeItemEntity(
                        eventId,
                        normalizedEvent.SourceTimestamp,
                        record)));
                break;
            case "book":
                dbContext.BookSnapshots.Add(new BookSnapshotEntity(
                    eventId,
                    normalizedEvent.Records.OfType<BookSnapshotRecord>().Single()));
                dbContext.BookLevels.AddRange(normalizedEvent.Records
                    .OfType<BookLevelRecord>()
                    .Select(record => new BookLevelEntity(eventId, record)));
                break;
            case "tick_size_change":
                dbContext.TickSizeChanges.Add(new TickSizeChangeEntity(
                    eventId,
                    (TickSizeChangeRecord)normalizedEvent.Records[0]));
                break;
            case "best_bid_ask":
                dbContext.BestBidAsks.Add(new BestBidAskEntity(
                    eventId,
                    (BestBidAskRecord)normalizedEvent.Records[0]));
                break;
            case "new_market":
                dbContext.NewMarkets.Add(new NewMarketEntity(
                    eventId,
                    normalizedEvent.Records.OfType<NewMarketRecord>().Single()));
                dbContext.NewMarketAssets.AddRange(normalizedEvent.Records
                    .OfType<NewMarketAssetRecord>()
                    .Select(record => new NewMarketAssetEntity(eventId, record)));
                break;
            case "market_resolved":
                dbContext.MarketResolutions.Add(new MarketResolutionEntity(
                    eventId,
                    normalizedEvent.Records.OfType<MarketResolvedRecord>().Single()));
                dbContext.MarketResolutionAssets.AddRange(normalizedEvent.Records
                    .OfType<MarketResolvedAssetRecord>()
                    .Select(record => new MarketResolutionAssetEntity(eventId, record)));
                break;
        }
    }

    private static void Validate(
        ClaimedRawMessage claim,
        NormalizationCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(claim.Message);
        ArgumentNullException.ThrowIfNull(completion);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(claim.ProjectionVersion);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(claim.AttemptCount);

        if (completion.Status is not (
            NormalizationStatus.Processed
            or NormalizationStatus.Invalid
            or NormalizationStatus.Unsupported
            or NormalizationStatus.Failed))
        {
            throw new ArgumentException("Completion status is not supported.", nameof(completion));
        }

        if (completion.Issue is not null
            && (completion.Issue.Code.Length > MaximumErrorCodeLength
                || completion.Issue.Message.Length > MaximumErrorMessageLength
                || completion.Issue.Field?.Length > MaximumErrorFieldLength))
        {
            throw new ArgumentException(
                "Normalization issue exceeds persistence limits.",
                nameof(completion));
        }

        foreach (var normalizedEvent in completion.Events)
        {
            if (normalizedEvent.RawMessageId != claim.Message.RawMessageId
                || normalizedEvent.ProjectionVersion != claim.ProjectionVersion
                || normalizedEvent.SessionId != claim.Message.SessionId
                || normalizedEvent.ReceivedAt != claim.Message.ReceivedAt)
            {
                throw new ArgumentException(
                    "Normalized event does not belong to the claimed message.",
                    nameof(completion));
            }

            ValidateRecordComposition(normalizedEvent);
        }
    }

    private static void ValidateRecordComposition(NormalizedEvent normalizedEvent)
    {
        var records = normalizedEvent.Records;
        var isValid = normalizedEvent.EventType switch
        {
            "last_trade_price" => records.Count == 1 && records[0] is LastTradeRecord,
            "price_change" => records.Count > 0 && records.All(record => record is PriceChangeRecord),
            "book" => records.Count(record => record is BookSnapshotRecord) == 1
                && records.All(record => record is BookSnapshotRecord or BookLevelRecord),
            "tick_size_change" => records.Count == 1 && records[0] is TickSizeChangeRecord,
            "best_bid_ask" => records.Count == 1 && records[0] is BestBidAskRecord,
            "new_market" => records.Count(record => record is NewMarketRecord) == 1
                && records.All(record => record is NewMarketRecord or NewMarketAssetRecord),
            "market_resolved" => records.Count(record => record is MarketResolvedRecord) == 1
                && records.All(record =>
                    record is MarketResolvedRecord or MarketResolvedAssetRecord),
            _ => false
        };

        if (!isValid)
        {
            throw new ArgumentException(
                $"Normalized records do not match event type '{normalizedEvent.EventType}'.",
                nameof(normalizedEvent));
        }
    }

    private static bool IsTerminal(NormalizationStatus status) => status is
        NormalizationStatus.Processed
        or NormalizationStatus.Unsupported
        or NormalizationStatus.Invalid
        or NormalizationStatus.Failed;

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private readonly record struct LedgerState(
        NormalizationStatus Status,
        int AttemptCount);

    private sealed record EventWrite(
        NormalizedEvent Event,
        NormalizedEventRecord Entity);
}
