using PolymarketLab.DataCollection.Core.Application.OrderBooks.Resynchronization.Models;

namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Resynchronization;

/// <summary>Восстанавливает локальное состояние стакана из полного REST-снимка.</summary>
public interface IOrderBookResynchronizer
{
    /// <summary>Восстанавливает состояние стакана из полного снимка внешнего источника.</summary>
    /// <param name="assetId">Идентификатор восстанавливаемого актива.</param>
    /// <param name="reason">Диагностическая причина запуска восстановления.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Результат восстановления с количеством попыток, снимком либо ошибкой.</returns>
    Task<OrderBookResyncResult> ResynchronizeAsync(
        string assetId,
        OrderBookResyncReason reason,
        CancellationToken cancellationToken);
}
