using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorOrderBookIntegrity;

/// <summary>Воспроизводит committed typed-события в изолированных временных состояниях стаканов.</summary>
public sealed class CollectorOrderBookIntegrityCoordinator(
    IOrderBookIntegrityReader reader,
    IOrderBookProjector projector,
    ILogger<CollectorOrderBookIntegrityCoordinator> logger)
    : ICollectorOrderBookIntegrityCoordinator
{
    /// <inheritdoc />
    public async Task<UnitResult<Error>> EvaluateAsync(
        CollectorSessionId sessionId,
        int projectionVersion,
        IReadOnlyCollection<TokenId> tokenIds,
        DateTimeOffset? eventStartsAt,
        DateTimeOffset? eventEndsAt,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(projectionVersion);
        ArgumentNullException.ThrowIfNull(tokenIds);

        try
        {
            var events = await reader.ReadAsync(
                sessionId,
                projectionVersion,
                cancellationToken);
            var expectedAssetIds = tokenIds
                .Select(tokenId => tokenId.Value)
                .ToHashSet(StringComparer.Ordinal);
            if (expectedAssetIds.Count == 0)
                throw new InvalidOperationException("Collector session has no snapshot tokens.");

            var states = expectedAssetIds.ToDictionary(
                assetId => assetId,
                assetId => new OrderBookState(assetId),
                StringComparer.Ordinal);
            var initializedAssetIds = new HashSet<string>(StringComparer.Ordinal);
            var sourceTimestampWatermarks =
                new Dictionary<(string AssetId, string EventType), OrderBookEventDiagnostic>();
            var previousEvents = new Dictionary<string, OrderBookEventDiagnostic>(StringComparer.Ordinal);

            foreach (var @event in events)
            {
                foreach (var assetId in GetAssetIds(@event))
                {
                    if (!states.TryGetValue(assetId, out var state))
                    {
                        return UnitResult.Failure(
                            CollectorOrderBookIntegrityErrors.UnexpectedAsset(
                                sessionId,
                                assetId));
                    }

                    if (@event is NormalizedOrderBookEvent.BookSnapshot)
                    {
                        var snapshotResult = projector.Apply(state, @event);
                        if (snapshotResult.IntegrityIssue is not null)
                        {
                            LogIntegrityIssue(
                                sessionId,
                                projectionVersion,
                                assetId,
                                @event,
                                state,
                                snapshotResult.IntegrityIssue,
                                sourceTimestampWatermarks,
                                previousEvents,
                                eventStartsAt,
                                eventEndsAt);
                            return UnitResult.Failure(
                                CollectorOrderBookIntegrityErrors.IntegrityIssue(
                                    sessionId,
                                    assetId,
                                    snapshotResult.IntegrityIssue));
                        }

                        initializedAssetIds.Add(assetId);
                        UpdateSourceTimestampWatermark(assetId, @event, sourceTimestampWatermarks);
                        previousEvents[assetId] = GetDiagnostic(@event, assetId);
                        continue;
                    }

                    if (!initializedAssetIds.Contains(assetId))
                    {
                        return UnitResult.Failure(
                            CollectorOrderBookIntegrityErrors.MissingSnapshot(
                                sessionId,
                                assetId));
                    }

                    var result = projector.Apply(state, @event);
                    if (result.IntegrityIssue is not null)
                    {
                        LogIntegrityIssue(
                            sessionId,
                            projectionVersion,
                            assetId,
                            @event,
                            state,
                            result.IntegrityIssue,
                            sourceTimestampWatermarks,
                            previousEvents,
                            eventStartsAt,
                            eventEndsAt);
                        return UnitResult.Failure(
                            CollectorOrderBookIntegrityErrors.IntegrityIssue(
                                sessionId,
                                assetId,
                                result.IntegrityIssue));
                    }

                    UpdateSourceTimestampWatermark(assetId, @event, sourceTimestampWatermarks);
                    previousEvents[assetId] = GetDiagnostic(@event, assetId);
                }
            }

            var missingAssetId = expectedAssetIds
                .FirstOrDefault(assetId => !initializedAssetIds.Contains(assetId));
            if (missingAssetId is not null)
            {
                return UnitResult.Failure(
                    CollectorOrderBookIntegrityErrors.MissingSnapshot(
                        sessionId,
                        missingAssetId));
            }

            return UnitResult.Success<Error>();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Failed to evaluate order book integrity for collector session {SessionId} and projection version {ProjectionVersion}.",
                sessionId.Value,
                projectionVersion);
            return UnitResult.Failure(
                CollectorOrderBookIntegrityErrors.ReadFailed(sessionId));
        }
    }

    private static IEnumerable<string> GetAssetIds(NormalizedOrderBookEvent @event) =>
        @event switch
        {
            NormalizedOrderBookEvent.BookSnapshot snapshot => [snapshot.Record.AssetId],
            NormalizedOrderBookEvent.PriceChanges changes => changes.Records
                .Select(change => change.AssetId)
                .Distinct(StringComparer.Ordinal),
            NormalizedOrderBookEvent.TickSizeChange change => [change.Record.AssetId],
            NormalizedOrderBookEvent.BestBidAsk quote => [quote.Record.AssetId],
            _ => throw new ArgumentOutOfRangeException(nameof(@event))
        };

    private void LogIntegrityIssue(
        CollectorSessionId sessionId,
        int projectionVersion,
        string assetId,
        NormalizedOrderBookEvent @event,
        OrderBookState state,
        OrderBookIntegrityIssue issue,
        IReadOnlyDictionary<(string AssetId, string EventType), OrderBookEventDiagnostic> sourceTimestampWatermarks,
        IReadOnlyDictionary<string, OrderBookEventDiagnostic> previousEvents,
        DateTimeOffset? eventStartsAt,
        DateTimeOffset? eventEndsAt)
    {
        if (issue.Type is not (OrderBookIntegrityIssueType.EventOrderViolation
            or OrderBookIntegrityIssueType.BestBidMismatch
            or OrderBookIntegrityIssueType.BestAskMismatch
            or OrderBookIntegrityIssueType.SpreadMismatch))
        {
            return;
        }

        var current = GetDiagnostic(@event, assetId);
        OrderBookEventDiagnostic? previous;
        if (issue.Type == OrderBookIntegrityIssueType.EventOrderViolation)
            sourceTimestampWatermarks.TryGetValue((assetId, current.EventType), out previous);
        else
            previousEvents.TryGetValue(assetId, out previous);
        var regressionMilliseconds = issue.Type == OrderBookIntegrityIssueType.EventOrderViolation
            ? previous?.SourceTimestamp - current.SourceTimestamp
            : null;
        var windowSegment = GetWindowSegment(current.ReceivedAt, eventStartsAt, eventEndsAt);

        logger.LogError(
            "Order book integrity issue. SessionId: {SessionId}, ProjectionVersion: {ProjectionVersion}, " +
            "AssetId: {AssetId}, EventType: {EventType}, NormalizedEventId: {NormalizedEventId}, " +
            "RawMessageId: {RawMessageId}, RawItemIndex: {RawItemIndex}, SourceTimestamp: {SourceTimestamp}, " +
            "PreviousEventType: {PreviousEventType}, PreviousNormalizedEventId: {PreviousNormalizedEventId}, " +
            "PreviousRawMessageId: {PreviousRawMessageId}, PreviousRawItemIndex: {PreviousRawItemIndex}, " +
            "PreviousSourceTimestamp: {PreviousSourceTimestamp}, RegressionMilliseconds: {RegressionMilliseconds}, " +
            "ConnectionEpoch: {ConnectionEpoch}, PreviousConnectionEpoch: {PreviousConnectionEpoch}, " +
            "ReceivedAt: {ReceivedAt}, PreviousReceivedAt: {PreviousReceivedAt}, WindowSegment: {WindowSegment}, " +
            "IssueType: {IssueType}, LocalBestBid: {LocalBestBid}, LocalBestAsk: {LocalBestAsk}, " +
            "LocalSpread: {LocalSpread}, EventBestBid: {EventBestBid}, EventBestAsk: {EventBestAsk}, " +
            "EventSpread: {EventSpread}, PreviousEventBestBid: {PreviousEventBestBid}, " +
            "PreviousEventBestAsk: {PreviousEventBestAsk}, PreviousEventSpread: {PreviousEventSpread}, " +
            "PriceChangeCount: {PriceChangeCount}, LastChangeItemIndex: {LastChangeItemIndex}, " +
            "LastChangeSide: {LastChangeSide}, LastChangePrice: {LastChangePrice}, LastChangeSize: {LastChangeSize}.",
            sessionId.Value,
            projectionVersion,
            assetId,
            current.EventType,
            current.Position.NormalizedEventId,
            current.Position.RawMessageId,
            current.Position.RawItemIndex,
            current.SourceTimestamp,
            previous?.EventType,
            previous?.Position.NormalizedEventId,
            previous?.Position.RawMessageId,
            previous?.Position.RawItemIndex,
            previous?.SourceTimestamp,
            regressionMilliseconds,
            current.ConnectionEpoch,
            previous?.ConnectionEpoch,
            current.ReceivedAt,
            previous?.ReceivedAt,
            windowSegment,
            issue.Type,
            state.BestBid,
            state.BestAsk,
            state.Spread,
            current.BestBid,
            current.BestAsk,
            current.Spread,
            previous?.BestBid,
            previous?.BestAsk,
            previous?.Spread,
            current.PriceChangeCount,
            current.LastChangeItemIndex,
            current.LastChangeSide,
            current.LastChangePrice,
            current.LastChangeSize);
    }

    private static void UpdateSourceTimestampWatermark(
        string assetId,
        NormalizedOrderBookEvent @event,
        IDictionary<(string AssetId, string EventType), OrderBookEventDiagnostic> sourceTimestampWatermarks)
    {
        var current = GetDiagnostic(@event, assetId);
        var watermarkKey = (assetId, current.EventType);
        if (current.SourceTimestamp.HasValue
            && (!sourceTimestampWatermarks.TryGetValue(watermarkKey, out var previous)
                || current.SourceTimestamp > previous.SourceTimestamp))
        {
            sourceTimestampWatermarks[watermarkKey] = current;
        }
    }

    private static OrderBookEventDiagnostic GetDiagnostic(
        NormalizedOrderBookEvent @event,
        string assetId) => @event switch
        {
            NormalizedOrderBookEvent.BookSnapshot snapshot => new(
                "book",
                snapshot.Record.Position,
                snapshot.Record.SourceTimestamp,
                snapshot.Record.ConnectionEpoch,
                snapshot.Record.ReceivedAt,
                snapshot.Record.Bids.Count == 0 ? null : snapshot.Record.Bids.Max(level => level.Price),
                snapshot.Record.Asks.Count == 0 ? null : snapshot.Record.Asks.Min(level => level.Price)),
            NormalizedOrderBookEvent.PriceChanges changes => GetPriceChangeDiagnostic(changes, assetId),
            NormalizedOrderBookEvent.TickSizeChange change => new(
                "tick_size_change",
                change.Record.Position,
                change.Record.SourceTimestamp,
                change.Record.ConnectionEpoch,
                change.Record.ReceivedAt,
                null,
                null),
            NormalizedOrderBookEvent.BestBidAsk quote => new(
                "best_bid_ask",
                quote.Record.Position,
                quote.Record.SourceTimestamp,
                quote.Record.ConnectionEpoch,
                quote.Record.ReceivedAt,
                quote.Record.BestBid,
                quote.Record.BestAsk,
                quote.Record.Spread),
            _ => throw new ArgumentOutOfRangeException(nameof(@event))
        };

    private static OrderBookEventDiagnostic GetPriceChangeDiagnostic(
        NormalizedOrderBookEvent.PriceChanges changes,
        string assetId)
    {
        var records = changes.Records
            .Where(change => string.Equals(change.AssetId, assetId, StringComparison.Ordinal))
            .OrderBy(change => change.ItemIndex)
            .ToArray();
        var record = records[^1];
        return new OrderBookEventDiagnostic(
            "price_change",
            record.Position,
            record.SourceTimestamp,
            record.ConnectionEpoch,
            record.ReceivedAt,
            record.BestBid,
            record.BestAsk,
            record.BestBid.HasValue && record.BestAsk.HasValue
                ? record.BestAsk.Value - record.BestBid.Value
                : null,
            PriceChangeCount: records.Length,
            LastChangeItemIndex: record.ItemIndex,
            LastChangeSide: record.Side,
            LastChangePrice: record.Price,
            LastChangeSize: record.Size);
    }

    private static string GetWindowSegment(
        DateTimeOffset? receivedAt,
        DateTimeOffset? eventStartsAt,
        DateTimeOffset? eventEndsAt)
    {
        if (!receivedAt.HasValue || !eventStartsAt.HasValue || !eventEndsAt.HasValue)
            return "Unknown";
        if (receivedAt < eventStartsAt)
            return "Preparation";
        return receivedAt < eventEndsAt ? "TradingWindow" : "Completion";
    }

    private sealed record OrderBookEventDiagnostic(
        string EventType,
        OrderBookEventPosition Position,
        long? SourceTimestamp,
        long? ConnectionEpoch,
        DateTimeOffset? ReceivedAt,
        decimal? BestBid,
        decimal? BestAsk,
        decimal? Spread = null,
        int? PriceChangeCount = null,
        int? LastChangeItemIndex = null,
        Application.Normalization.Models.TradeSide? LastChangeSide = null,
        decimal? LastChangePrice = null,
        decimal? LastChangeSize = null);
}
