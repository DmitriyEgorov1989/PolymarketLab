namespace PolymarketLab.DataCollection.Core.Application.Normalization.Models;

/// <summary>Уровень одной стороны снимка стакана.</summary>
public sealed record BookLevelRecord : NormalizedRecord
{
    /// <summary>Создаёт нормализованную запись уровня стакана.</summary>
    /// <param name="side">Сторона стакана.</param>
    /// <param name="levelIndex">Позиция уровня в исходном массиве стороны.</param>
    /// <param name="price">Цена уровня в диапазоне от нуля до единицы.</param>
    /// <param name="size">Размер уровня.</param>
    public BookLevelRecord(
        OrderBookSide side,
        int levelIndex,
        decimal price,
        decimal size)
    {
        if (!Enum.IsDefined(side))
            throw new ArgumentOutOfRangeException(nameof(side), "Order book side is not supported.");

        if (levelIndex < 0)
            throw new ArgumentOutOfRangeException(
                nameof(levelIndex),
                "Book level index cannot be negative.");

        if (price is < 0 or > 1)
            throw new ArgumentOutOfRangeException(
                nameof(price),
                "Book level price must be between zero and one.");

        if (size < 0)
            throw new ArgumentOutOfRangeException(
                nameof(size),
                "Book level size cannot be negative.");

        Side = side;
        LevelIndex = levelIndex;
        Price = price;
        Size = size;
    }

    /// <summary>Сторона стакана.</summary>
    public OrderBookSide Side { get; }

    /// <summary>Позиция уровня в исходном массиве стороны.</summary>
    public int LevelIndex { get; }

    /// <summary>Цена уровня в диапазоне от нуля до единицы.</summary>
    public decimal Price { get; }

    /// <summary>Неотрицательный размер уровня.</summary>
    public decimal Size { get; }
}
