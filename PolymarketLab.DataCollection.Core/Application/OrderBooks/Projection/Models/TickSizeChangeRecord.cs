namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;

/// <summary>Нормализованное изменение шага цены для последовательного применения Projector.</summary>
public sealed record TickSizeChangeRecord
{
    /// <summary>Создаёт входную модель изменения шага цены.</summary>
    /// <param name="rawMessageId">Идентификатор исходного сообщения в архиве.</param>
    /// <param name="rawItemIndex">Позиция логического события внутри исходного сообщения.</param>
    /// <param name="normalizedEventId">Идентификатор сохранённого нормализованного события.</param>
    /// <param name="assetId">Идентификатор актива.</param>
    /// <param name="sourceTimestamp">Epoch milliseconds из исходного события или <see langword="null" />.</param>
    /// <param name="oldTickSize">Предыдущий шаг цены.</param>
    /// <param name="newTickSize">Новый положительный шаг цены.</param>
    public TickSizeChangeRecord(
        long rawMessageId,
        int rawItemIndex,
        long normalizedEventId,
        string assetId,
        long? sourceTimestamp,
        decimal oldTickSize,
        decimal newTickSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);

        if (newTickSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(newTickSize), "New tick size must be positive.");

        Position = new OrderBookEventPosition(rawMessageId, rawItemIndex, normalizedEventId);
        AssetId = assetId;
        SourceTimestamp = sourceTimestamp;
        OldTickSize = oldTickSize;
        NewTickSize = newTickSize;
    }

    /// <summary>Позиция события в нормализованном архиве.</summary>
    public OrderBookEventPosition Position { get; }

    /// <summary>Идентификатор сохранённого нормализованного события.</summary>
    public long NormalizedEventId => Position.NormalizedEventId;

    /// <summary>Идентификатор актива.</summary>
    public string AssetId { get; }

    /// <summary>Epoch milliseconds из исходного события или <see langword="null" />.</summary>
    public long? SourceTimestamp { get; }

    /// <summary>Предыдущий шаг цены.</summary>
    public decimal OldTickSize { get; }

    /// <summary>Новый положительный шаг цены.</summary>
    public decimal NewTickSize { get; }
}
