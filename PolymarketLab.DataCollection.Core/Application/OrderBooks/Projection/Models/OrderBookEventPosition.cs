namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;

/// <summary>Устойчивая позиция логического события в нормализованном архиве.</summary>
public sealed record OrderBookEventPosition : IComparable<OrderBookEventPosition>
{
    /// <summary>Создаёт позицию логического события в нормализованном архиве.</summary>
    /// <param name="rawMessageId">Идентификатор исходного сообщения.</param>
    /// <param name="rawItemIndex">Позиция логического события внутри исходного сообщения.</param>
    /// <param name="normalizedEventId">Идентификатор сохранённого нормализованного события.</param>
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

    /// <summary>Идентификатор исходного сообщения.</summary>
    public long RawMessageId { get; }

    /// <summary>Позиция логического события внутри исходного сообщения.</summary>
    public int RawItemIndex { get; }

    /// <summary>Идентификатор сохранённого нормализованного события.</summary>
    public long NormalizedEventId { get; }

    /// <inheritdoc />
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
