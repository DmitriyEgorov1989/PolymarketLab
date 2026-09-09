using Microsoft.EntityFrameworkCore;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using ProjectionBestBidAskRecord = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models.BestBidAskRecord;
using ProjectionBookSnapshotRecord = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models.BookSnapshotRecord;
using ProjectionPriceChangeRecord = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models.PriceChangeRecord;
using ProjectionTickSizeChangeRecord = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models.TickSizeChangeRecord;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Normalization;

/// <summary>
/// Читает committed typed-проекции стакана только для заданных collector session
/// и snapshot-версии и восстанавливает существующие модели projector.
/// </summary>
public sealed class OrderBookIntegrityReader(DataCollectionDbContext dbContext)
    : IOrderBookIntegrityReader
{
    private static readonly string[] EventTypes =
        ["book", "price_change", "tick_size_change", "best_bid_ask"];

    /// <inheritdoc />
    public async Task<IReadOnlyList<NormalizedOrderBookEvent>> ReadAsync(
        CollectorSessionId sessionId,
        int projectionVersion,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(projectionVersion);

        var headers = await (
                from normalized in dbContext.NormalizedEvents.AsNoTracking()
                join raw in dbContext.RawMarketMessages.AsNoTracking()
                    on normalized.RawMessageId equals raw.Id
                where raw.SessionId == sessionId
                    && normalized.SessionId == sessionId
                    && normalized.ProjectionVersion == projectionVersion
                    && EventTypes.Contains(normalized.EventType)
                orderby normalized.RawMessageId, normalized.RawItemIndex
                select new EventHeader(
                    normalized.Id,
                    normalized.RawMessageId,
                    normalized.RawItemIndex,
                    normalized.EventType,
                    normalized.SourceTimestamp,
                    normalized.MarketConditionId,
                    normalized.AssetId))
            .ToListAsync(cancellationToken);

        if (headers.Count == 0)
            return [];

        var eventIds = headers.Select(header => header.EventId).ToArray();
        var snapshots = await dbContext.BookSnapshots
            .AsNoTracking()
            .Where(snapshot => eventIds.Contains(snapshot.EventId))
            .ToDictionaryAsync(snapshot => snapshot.EventId, cancellationToken);
        var levels = await dbContext.BookLevels
            .AsNoTracking()
            .Where(level => eventIds.Contains(level.EventId))
            .OrderBy(level => level.EventId)
            .ThenBy(level => level.Side)
            .ThenBy(level => level.LevelIndex)
            .ToListAsync(cancellationToken);
        var priceChanges = await dbContext.PriceChanges
            .AsNoTracking()
            .Where(change => eventIds.Contains(change.EventId))
            .OrderBy(change => change.EventId)
            .ThenBy(change => change.ItemIndex)
            .ToListAsync(cancellationToken);
        var tickSizeChanges = await dbContext.TickSizeChanges
            .AsNoTracking()
            .Where(change => eventIds.Contains(change.EventId))
            .ToDictionaryAsync(change => change.EventId, cancellationToken);
        var bestBidAsks = await dbContext.BestBidAsks
            .AsNoTracking()
            .Where(quote => eventIds.Contains(quote.EventId))
            .ToDictionaryAsync(quote => quote.EventId, cancellationToken);

        var levelsByEvent = levels.ToLookup(level => level.EventId);
        var priceChangesByEvent = priceChanges.ToLookup(change => change.EventId);
        return headers.Select(header => header.EventType switch
        {
            "book" => MapBook(header, snapshots[header.EventId], levelsByEvent[header.EventId]),
            "price_change" => MapPriceChanges(header, priceChangesByEvent[header.EventId]),
            "tick_size_change" => MapTickSizeChange(
                header,
                tickSizeChanges[header.EventId]),
            "best_bid_ask" => MapBestBidAsk(header, bestBidAsks[header.EventId]),
            _ => throw new InvalidOperationException("Order book event type is not supported.")
        }).ToArray();
    }

    private static NormalizedOrderBookEvent MapBook(
        EventHeader header,
        Models.BookSnapshotEntity snapshot,
        IEnumerable<Models.BookLevelEntity> levels)
    {
        var assetId = Require(header.AssetId, "asset_id");
        var marketConditionId = Require(header.MarketConditionId, "market_condition_id");
        var records = levels
            .Select(level => new BookLevelRecord(
                level.Side,
                level.LevelIndex,
                level.Price,
                level.Size))
            .ToArray();
        return new NormalizedOrderBookEvent.BookSnapshot(new ProjectionBookSnapshotRecord(
            header.RawMessageId,
            header.RawItemIndex,
            header.EventId,
            assetId,
            marketConditionId,
            header.SourceTimestamp,
            snapshot.Hash,
            snapshot.TickSize,
            records.Where(level => level.Side == OrderBookSide.Bid).ToArray(),
            records.Where(level => level.Side == OrderBookSide.Ask).ToArray()));
    }

    private static NormalizedOrderBookEvent MapPriceChanges(
        EventHeader header,
        IEnumerable<Models.PriceChangeItemEntity> changes) =>
        new NormalizedOrderBookEvent.PriceChanges(changes.Select(change =>
            new ProjectionPriceChangeRecord(
                header.RawMessageId,
                header.RawItemIndex,
                header.EventId,
                change.AssetId,
                change.SourceTimestamp,
                change.Side,
                change.Price,
                change.Size,
                change.Hash,
                change.BestBid,
                change.BestAsk,
                change.ItemIndex)).ToArray());

    private static NormalizedOrderBookEvent MapTickSizeChange(
        EventHeader header,
        Models.TickSizeChangeEntity change) =>
        new NormalizedOrderBookEvent.TickSizeChange(new ProjectionTickSizeChangeRecord(
            header.RawMessageId,
            header.RawItemIndex,
            header.EventId,
            Require(header.AssetId, "asset_id"),
            header.SourceTimestamp,
            change.OldTickSize,
            change.NewTickSize));

    private static NormalizedOrderBookEvent MapBestBidAsk(
        EventHeader header,
        Models.BestBidAskEntity quote) =>
        new NormalizedOrderBookEvent.BestBidAsk(new ProjectionBestBidAskRecord(
            header.RawMessageId,
            header.RawItemIndex,
            header.EventId,
            Require(header.AssetId, "asset_id"),
            header.SourceTimestamp,
            quote.BestBid,
            quote.BestAsk,
            quote.Spread));

    private static string Require(string? value, string field) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"Normalized order book event has no required {field}.");

    private sealed record EventHeader(
        long EventId,
        long RawMessageId,
        int RawItemIndex,
        string EventType,
        long? SourceTimestamp,
        string? MarketConditionId,
        string? AssetId);
}
