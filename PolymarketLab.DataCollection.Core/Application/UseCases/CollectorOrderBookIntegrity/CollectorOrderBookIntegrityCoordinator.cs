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
                            LogEventOrderViolation(
                                sessionId,
                                projectionVersion,
                                assetId,
                                @event,
                                snapshotResult.IntegrityIssue,
                                sourceTimestampWatermarks,
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
                        LogEventOrderViolation(
                            sessionId,
                            projectionVersion,
                            assetId,
                            @event,
                            result.IntegrityIssue,
                            sourceTimestampWatermarks,
                            eventStartsAt,
                            eventEndsAt);
                        return UnitResult.Failure(
                            CollectorOrderBookIntegrityErrors.IntegrityIssue(
                                sessionId,
                                assetId,
                                result.IntegrityIssue));
                    }

                    UpdateSourceTimestampWatermark(assetId, @event, sourceTimestampWatermarks);
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

    private void LogEventOrderViolation(
        CollectorSessionId sessionId,
        int projectionVersion,
        string assetId,
        NormalizedOrderBookEvent @event,
        OrderBookIntegrityIssue issue,
        IReadOnlyDictionary<(string AssetId, string EventType), OrderBookEventDiagnostic> sourceTimestampWatermarks,
        DateTimeOffset? eventStartsAt,
        DateTimeOffset? eventEndsAt)
    {
        if (issue.Type != OrderBookIntegrityIssueType.EventOrderViolation)
            return;

        var current = GetDiagnostic(@event, assetId);
        sourceTimestampWatermarks.TryGetValue((assetId, current.EventType), out var previous);
        var regressionMilliseconds = previous?.SourceTimestamp - current.SourceTimestamp;
        var windowSegment = GetWindowSegment(current.ReceivedAt, eventStartsAt, eventEndsAt);

        logger.LogError(
            "Order book event order violation. SessionId: {SessionId}, ProjectionVersion: {ProjectionVersion}, " +
            "AssetId: {AssetId}, EventType: {EventType}, NormalizedEventId: {NormalizedEventId}, " +
            "RawMessageId: {RawMessageId}, RawItemIndex: {RawItemIndex}, SourceTimestamp: {SourceTimestamp}, " +
            "PreviousEventType: {PreviousEventType}, PreviousNormalizedEventId: {PreviousNormalizedEventId}, " +
            "PreviousRawMessageId: {PreviousRawMessageId}, PreviousRawItemIndex: {PreviousRawItemIndex}, " +
            "PreviousSourceTimestamp: {PreviousSourceTimestamp}, RegressionMilliseconds: {RegressionMilliseconds}, " +
            "ConnectionEpoch: {ConnectionEpoch}, PreviousConnectionEpoch: {PreviousConnectionEpoch}, " +
            "ReceivedAt: {ReceivedAt}, PreviousReceivedAt: {PreviousReceivedAt}, WindowSegment: {WindowSegment}.",
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
            windowSegment);
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
                snapshot.Record.ReceivedAt),
            NormalizedOrderBookEvent.PriceChanges changes => GetPriceChangeDiagnostic(changes, assetId),
            NormalizedOrderBookEvent.TickSizeChange change => new(
                "tick_size_change",
                change.Record.Position,
                change.Record.SourceTimestamp,
                change.Record.ConnectionEpoch,
                change.Record.ReceivedAt),
            NormalizedOrderBookEvent.BestBidAsk quote => new(
                "best_bid_ask",
                quote.Record.Position,
                quote.Record.SourceTimestamp,
                quote.Record.ConnectionEpoch,
                quote.Record.ReceivedAt),
            _ => throw new ArgumentOutOfRangeException(nameof(@event))
        };

    private static OrderBookEventDiagnostic GetPriceChangeDiagnostic(
        NormalizedOrderBookEvent.PriceChanges changes,
        string assetId)
    {
        var record = changes.Records.First(change =>
            string.Equals(change.AssetId, assetId, StringComparison.Ordinal));
        return new OrderBookEventDiagnostic(
            "price_change",
            record.Position,
            record.SourceTimestamp,
            record.ConnectionEpoch,
            record.ReceivedAt);
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
        DateTimeOffset? ReceivedAt);
}
