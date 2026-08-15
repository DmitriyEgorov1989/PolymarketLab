namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;

/// <summary>Нарушение целостности текущего состояния стакана.</summary>
public enum OrderBookIntegrityIssue
{
    /// <summary>Лучшая цена покупки выше лучшей цены продажи.</summary>
    CrossedBook = 1
}
