namespace PolymarketLab.DataCollection.Core.Application.Normalization.Models;

/// <summary>Изменение шага цены актива.</summary>
public sealed record TickSizeChangeRecord : NormalizedRecord
{
    /// <summary>Создаёт запись изменения шага цены.</summary>
    /// <param name="oldTickSize">Предыдущий шаг цены.</param>
    /// <param name="newTickSize">Новый положительный шаг цены.</param>
    public TickSizeChangeRecord(decimal oldTickSize, decimal newTickSize)
    {
        if (newTickSize <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(newTickSize),
                "New tick size must be positive.");

        OldTickSize = oldTickSize;
        NewTickSize = newTickSize;
    }

    /// <summary>Предыдущий шаг цены.</summary>
    public decimal OldTickSize { get; }

    /// <summary>Новый положительный шаг цены.</summary>
    public decimal NewTickSize { get; }
}
