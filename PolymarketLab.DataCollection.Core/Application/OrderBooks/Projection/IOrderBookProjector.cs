using PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;

namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection;

/// <summary>Последовательно применяет нормализованные события к состоянию стакана.</summary>
public interface IOrderBookProjector
{
    /// <summary>Последовательно применяет одно нормализованное событие к состоянию стакана.</summary>
    /// <param name="state">Изменяемое состояние одного актива.</param>
    /// <param name="event">Нормализованное событие стакана.</param>
    /// <returns>Итог применения и актуальная диагностика целостности.</returns>
    OrderBookProjectionResult Apply(
        OrderBookState state,
        NormalizedOrderBookEvent @event);
}
