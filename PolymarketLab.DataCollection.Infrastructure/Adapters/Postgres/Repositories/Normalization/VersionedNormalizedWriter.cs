using System.Data.Common;
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

    public async Task<NormalizationWriteStatus> WriteAsync(
        ClaimedRawMessage claim,
        NormalizationCompletion completion,
        CancellationToken cancellationToken)
    {
        Validate(claim, completion);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        try
        {
            var fencedSessions = await CollectorSessionWriteFence.LockAsync(
                dbContext,
                transaction,
                [claim.Message.SessionId],
                cancellationToken);
            if (fencedSessions.Contains(claim.Message.SessionId))
            {
                await transaction.RollbackAsync(cancellationToken);
                return NormalizationWriteStatus.ClaimLost;
            }

            var ledger = await LockLedgerAsync(claim, transaction, cancellationToken);
            if (ledger is null
                || ledger.Value.Status != NormalizationStatus.Processing
                || ledger.Value.AttemptCount != claim.AttemptCount)
            {
                var result = ledger is not null && IsTerminal(ledger.Value.Status)
                    ? NormalizationWriteStatus.AlreadyCompleted
                    : NormalizationWriteStatus.ClaimLost;
                await transaction.RollbackAsync(cancellationToken);
                return result;
            }

            var normalizedAt = timeProvider.GetUtcNow();
            var events = completion.Events
                .Select(normalizedEvent => new EventWrite(
                    normalizedEvent,
                    new NormalizedEventRecord(normalizedEvent, normalizedAt)))
                .ToArray();
            dbContext.NormalizedEvents.AddRange(events.Select(item => item.Entity));
            await dbContext.SaveChangesAsync(cancellationToken);

            foreach (var item in events)
                AddTypedRows(item.Event, item.Entity.Id);

            await dbContext.SaveChangesAsync(cancellationToken);
            var affected = await CompleteLedgerAsync(claim, completion, cancellationToken);
            if (affected != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
                return NormalizationWriteStatus.ClaimLost;
            }

            await transaction.CommitAsync(cancellationToken);
            return NormalizationWriteStatus.Written;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<LedgerState?> LockLedgerAsync(
        ClaimedRawMessage claim,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            """
            SELECT status, attempt_count
            FROM data_collection.raw_message_normalizations
            WHERE raw_message_id = @raw_message_id
              AND projection_version = @projection_version
            FOR UPDATE
            """;
        AddParameter(command, "raw_message_id", claim.Message.RawMessageId);
        AddParameter(command, "projection_version", claim.ProjectionVersion);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new LedgerState(
            (NormalizationStatus)reader.GetInt32(0),
            reader.GetInt32(1));
    }

    private Task<int> CompleteLedgerAsync(
        ClaimedRawMessage claim,
        NormalizationCompletion completion,
        CancellationToken cancellationToken)
    {
        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE data_collection.raw_message_normalizations
            SET status = {(int)completion.Status},
                completed_at = CURRENT_TIMESTAMP,
                error_code = {(completion.Issue == null ? null : completion.Issue.Code)},
                error_message = {(completion.Issue == null ? null : completion.Issue.Message)},
                error_field = {(completion.Issue == null ? null : completion.Issue.Field)}
            WHERE raw_message_id = {claim.Message.RawMessageId}
              AND projection_version = {claim.ProjectionVersion}
              AND status = {(int)NormalizationStatus.Processing}
              AND attempt_count = {claim.AttemptCount}
            """,
            cancellationToken);
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
