using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Ports;

/// <summary>Получает полный актуальный снимок стакана из внешнего источника.</summary>
public interface IOrderBookSnapshotSource
{
    Task<Result<OrderBookSnapshot, Error>> GetAsync(
        string assetId,
        CancellationToken cancellationToken);
}
