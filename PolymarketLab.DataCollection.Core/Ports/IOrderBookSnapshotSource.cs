using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Ports;

/// <summary>Получает полный актуальный снимок стакана из внешнего источника.</summary>
public interface IOrderBookSnapshotSource
{
    /// <summary>Получает полный актуальный снимок стакана указанного актива.</summary>
    /// <param name="assetId">Внешний идентификатор актива.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Снимок стакана либо диагностированная ошибка внешнего источника.</returns>
    Task<Result<OrderBookSnapshot, Error>> GetAsync(
        string assetId,
        CancellationToken cancellationToken);
}
