using PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;

namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection;

/// <summary>Чистый синхронный проектор нормализованных событий стакана.</summary>
public sealed class OrderBookProjector : IOrderBookProjector
{
    /// <inheritdoc />
    public OrderBookProjectionResult Apply(
        OrderBookState state,
        NormalizedOrderBookEvent @event)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(@event, "event");

        var previousPosition = state.EventPosition;

        switch (@event)
        {
            case NormalizedOrderBookEvent.BookSnapshot snapshot
                when IsStateAsset(state, snapshot.Record.AssetId):
                state.Apply(snapshot.Record);
                break;
            case NormalizedOrderBookEvent.BookSnapshot:
                return OrderBookProjectionResult.Ignored(state.IntegrityIssue);

            case NormalizedOrderBookEvent.PriceChanges priceChanges:
                var applicableChanges = priceChanges.Records
                    .Where(change => IsStateAsset(state, change.AssetId))
                    .ToArray();
                if (applicableChanges.Length == 0)
                    return OrderBookProjectionResult.Ignored(state.IntegrityIssue);

                state.Apply(applicableChanges);
                break;

            case NormalizedOrderBookEvent.TickSizeChange tickSizeChange
                when IsStateAsset(state, tickSizeChange.Record.AssetId):
                state.Apply(tickSizeChange.Record);
                break;
            case NormalizedOrderBookEvent.TickSizeChange:
                return OrderBookProjectionResult.Ignored(state.IntegrityIssue);

            case NormalizedOrderBookEvent.BestBidAsk bestBidAsk
                when IsStateAsset(state, bestBidAsk.Record.AssetId):
                state.Apply(bestBidAsk.Record);
                break;
            case NormalizedOrderBookEvent.BestBidAsk:
                return OrderBookProjectionResult.Ignored(state.IntegrityIssue);

            default:
                throw new ArgumentOutOfRangeException(nameof(@event), "Order book event type is not supported.");
        }

        return state.EventPosition?.CompareTo(previousPosition) > 0
            ? OrderBookProjectionResult.Applied(state.IntegrityIssue)
            : OrderBookProjectionResult.Ignored(state.IntegrityIssue);
    }

    private static bool IsStateAsset(OrderBookState state, string assetId)
    {
        return string.Equals(state.AssetId, assetId, StringComparison.Ordinal);
    }
}
