namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;

/// <summary>Агрегированный уровень текущего состояния стакана.</summary>
/// <param name="Price">Неотрицательная цена уровня.</param>
/// <param name="Size">Неотрицательный размер уровня.</param>
public readonly record struct OrderBookLevel(decimal Price, decimal Size)
{
    /// <summary>Неотрицательная цена уровня.</summary>
    public decimal Price { get; init; } = Price >= 0
        ? Price
        : throw new ArgumentOutOfRangeException(nameof(Price), "Price cannot be negative.");

    /// <summary>Неотрицательный размер уровня.</summary>
    public decimal Size { get; init; } = Size >= 0
        ? Size
        : throw new ArgumentOutOfRangeException(nameof(Size), "Size cannot be negative.");
}
