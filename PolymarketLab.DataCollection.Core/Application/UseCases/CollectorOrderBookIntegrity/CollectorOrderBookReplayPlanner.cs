using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorOrderBookIntegrity;

/// <summary>Строит независимый source-ordered terminal replay для каждого актива.</summary>
internal static class CollectorOrderBookReplayPlanner
{
    /// <summary>
    /// Сохраняет архивный provenance, но назначает событиям монотонные позиции ephemeral replay.
    /// События с неизвестным timestamp или epoch и полные снимки являются границами сортировки.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<CollectorOrderBookReplayEvent>> Plan(
        IReadOnlyList<NormalizedOrderBookEvent> events,
        IReadOnlyCollection<string> assetIds)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(assetIds);

        var archivedByAsset = assetIds.ToDictionary(
            assetId => assetId,
            _ => new List<ArchivedEvent>(),
            StringComparer.Ordinal);
        var archiveOrdinal = 0;
        foreach (var @event in events)
        {
            foreach (var assetEvent in SplitByAsset(@event))
            {
                if (!archivedByAsset.TryGetValue(assetEvent.AssetId, out var archivedEvents))
                    throw new InvalidOperationException($"Unexpected order book asset '{assetEvent.AssetId}'.");

                archivedEvents.Add(CreateArchivedEvent(assetEvent.Event, archiveOrdinal++));
            }
        }

        return archivedByAsset.ToDictionary(
            item => item.Key,
            item => (IReadOnlyList<CollectorOrderBookReplayEvent>)PlanAsset(item.Value),
            StringComparer.Ordinal);
    }

    private static IReadOnlyList<CollectorOrderBookReplayEvent> PlanAsset(
        IReadOnlyList<ArchivedEvent> archivedEvents)
    {
        var ordered = new List<ArchivedEvent>(archivedEvents.Count);
        var sortableRun = new List<ArchivedEvent>();
        long? runEpoch = null;

        foreach (var archivedEvent in archivedEvents)
        {
            if (archivedEvent.IsSnapshot
                || !archivedEvent.SourceTimestamp.HasValue
                || !archivedEvent.ConnectionEpoch.HasValue)
            {
                FlushRun(sortableRun, ordered);
                ordered.Add(archivedEvent);
                runEpoch = null;
                continue;
            }

            if (sortableRun.Count > 0
                && runEpoch != archivedEvent.ConnectionEpoch)
            {
                FlushRun(sortableRun, ordered);
            }

            runEpoch = archivedEvent.ConnectionEpoch;
            sortableRun.Add(archivedEvent);
        }

        FlushRun(sortableRun, ordered);

        var replayEvents = new CollectorOrderBookReplayEvent[ordered.Count];
        for (var index = 0; index < ordered.Count; index++)
        {
            var original = ordered[index].Event;
            replayEvents[index] = new CollectorOrderBookReplayEvent(
                original,
                WithReplayPosition(original, index + 1L));
        }

        return replayEvents;
    }

    private static void FlushRun(
        ICollection<ArchivedEvent> sortableRun,
        ICollection<ArchivedEvent> destination)
    {
        foreach (var archivedEvent in sortableRun
                     .OrderBy(item => item.SourceTimestamp)
                     .ThenBy(item => item.Position.RawMessageId)
                     .ThenBy(item => item.Position.RawItemIndex)
                     .ThenBy(item => item.ArchiveOrdinal))
        {
            destination.Add(archivedEvent);
        }

        sortableRun.Clear();
    }

    private static IEnumerable<AssetEvent> SplitByAsset(NormalizedOrderBookEvent @event) =>
        @event switch
        {
            NormalizedOrderBookEvent.BookSnapshot snapshot =>
            [new AssetEvent(snapshot.Record.AssetId, snapshot)],
            NormalizedOrderBookEvent.PriceChanges changes => changes.Records
                .GroupBy(change => change.AssetId, StringComparer.Ordinal)
                .Select(group => new AssetEvent(
                    group.Key,
                    new NormalizedOrderBookEvent.PriceChanges(group.ToArray()))),
            NormalizedOrderBookEvent.TickSizeChange change =>
            [new AssetEvent(change.Record.AssetId, change)],
            NormalizedOrderBookEvent.BestBidAsk quote =>
            [new AssetEvent(quote.Record.AssetId, quote)],
            _ => throw new ArgumentOutOfRangeException(nameof(@event))
        };

    private static ArchivedEvent CreateArchivedEvent(
        NormalizedOrderBookEvent @event,
        int archiveOrdinal) => @event switch
        {
            NormalizedOrderBookEvent.BookSnapshot snapshot => new ArchivedEvent(
                @event,
                snapshot.Record.Position,
                snapshot.Record.SourceTimestamp,
                snapshot.Record.ConnectionEpoch,
                true,
                archiveOrdinal),
            NormalizedOrderBookEvent.PriceChanges changes => new ArchivedEvent(
                @event,
                changes.Records[0].Position,
                changes.Records[0].SourceTimestamp,
                changes.Records[0].ConnectionEpoch,
                false,
                archiveOrdinal),
            NormalizedOrderBookEvent.TickSizeChange change => new ArchivedEvent(
                @event,
                change.Record.Position,
                change.Record.SourceTimestamp,
                change.Record.ConnectionEpoch,
                false,
                archiveOrdinal),
            NormalizedOrderBookEvent.BestBidAsk quote => new ArchivedEvent(
                @event,
                quote.Record.Position,
                quote.Record.SourceTimestamp,
                quote.Record.ConnectionEpoch,
                false,
                archiveOrdinal),
            _ => throw new ArgumentOutOfRangeException(nameof(@event))
        };

    private static NormalizedOrderBookEvent WithReplayPosition(
        NormalizedOrderBookEvent @event,
        long replayPosition) => @event switch
        {
            NormalizedOrderBookEvent.BookSnapshot snapshot =>
                new NormalizedOrderBookEvent.BookSnapshot(new BookSnapshotRecord(
                    replayPosition,
                    0,
                    snapshot.Record.NormalizedEventId,
                    snapshot.Record.AssetId,
                    snapshot.Record.MarketConditionId,
                    snapshot.Record.SourceTimestamp,
                    snapshot.Record.Hash,
                    snapshot.Record.TickSize,
                    snapshot.Record.Bids,
                    snapshot.Record.Asks,
                    snapshot.Record.ConnectionEpoch,
                    snapshot.Record.ReceivedAt)),
            NormalizedOrderBookEvent.PriceChanges changes =>
                new NormalizedOrderBookEvent.PriceChanges(changes.Records
                    .Select(change => new PriceChangeRecord(
                        replayPosition,
                        0,
                        change.NormalizedEventId,
                        change.AssetId,
                        change.SourceTimestamp,
                        change.Side,
                        change.Price,
                        change.Size,
                        change.Hash,
                        change.BestBid,
                        change.BestAsk,
                        change.ItemIndex,
                        change.ConnectionEpoch,
                        change.ReceivedAt))
                    .ToArray()),
            NormalizedOrderBookEvent.TickSizeChange change =>
                new NormalizedOrderBookEvent.TickSizeChange(new TickSizeChangeRecord(
                    replayPosition,
                    0,
                    change.Record.NormalizedEventId,
                    change.Record.AssetId,
                    change.Record.SourceTimestamp,
                    change.Record.OldTickSize,
                    change.Record.NewTickSize,
                    change.Record.ConnectionEpoch,
                    change.Record.ReceivedAt)),
            NormalizedOrderBookEvent.BestBidAsk quote =>
                new NormalizedOrderBookEvent.BestBidAsk(new BestBidAskRecord(
                    replayPosition,
                    0,
                    quote.Record.NormalizedEventId,
                    quote.Record.AssetId,
                    quote.Record.SourceTimestamp,
                    quote.Record.BestBid,
                    quote.Record.BestAsk,
                    quote.Record.Spread,
                    quote.Record.ConnectionEpoch,
                    quote.Record.ReceivedAt)),
            _ => throw new ArgumentOutOfRangeException(nameof(@event))
        };

    private sealed record AssetEvent(string AssetId, NormalizedOrderBookEvent Event);

    private sealed record ArchivedEvent(
        NormalizedOrderBookEvent Event,
        OrderBookEventPosition Position,
        long? SourceTimestamp,
        long? ConnectionEpoch,
        bool IsSnapshot,
        int ArchiveOrdinal);
}

/// <summary>Связывает исходное архивное событие с его ephemeral-представлением для terminal replay.</summary>
internal sealed record CollectorOrderBookReplayEvent(
    NormalizedOrderBookEvent OriginalEvent,
    NormalizedOrderBookEvent ProjectableEvent);
