namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;

/// <summary>Сторона уровня в текущем состоянии стакана.</summary>
public enum OrderBookSide
{
    /// <summary>Сторона заявок на покупку.</summary>
    Bid = 1,

    /// <summary>Сторона заявок на продажу.</summary>
    Ask = 2
}
