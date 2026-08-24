namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;

/// <summary>Результат обработки события проектором стакана.</summary>
public enum OrderBookProjectionOutcome
{
    /// <summary>Событие относится к активу и изменило состояние или его диагностику.</summary>
    Applied = 1,

    /// <summary>Событие не относится к состоянию либо не продвинуло его позицию.</summary>
    Ignored = 2
}
