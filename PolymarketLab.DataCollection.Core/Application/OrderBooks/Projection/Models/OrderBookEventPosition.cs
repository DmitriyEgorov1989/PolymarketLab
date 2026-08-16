namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;

/// <summary>Устойчивая позиция логического события в нормализованном архиве.</summary>
public sealed record OrderBookEventPosition : IComparable<OrderBookEventPosition>
{
    public OrderBookEventPosition(
        long rawMessageId,
        int rawItemIndex,
        long normalizedEventId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rawMessageId);
        ArgumentOutOfRangeException.ThrowIfNegative(rawItemIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(normalizedEventId);

        RawMessageId = rawMessageId;
        RawItemIndex = rawItemIndex;
        NormalizedEventId = normalizedEventId;
    }

    public long RawMessageId { get; }

    public int RawItemIndex { get; }

    public long NormalizedEventId { get; }

    public int CompareTo(OrderBookEventPosition? other)
    {
        if (other is null)
            return 1;

        var rawMessageComparison = RawMessageId.CompareTo(other.RawMessageId);
        if (rawMessageComparison != 0)
            return rawMessageComparison;

        return RawItemIndex.CompareTo(other.RawItemIndex);
    }
}
