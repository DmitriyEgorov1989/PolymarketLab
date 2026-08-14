using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

internal sealed class LastTradePriceEntity
{
    private LastTradePriceEntity()
    {
    }

    public LastTradePriceEntity(long eventId, LastTradeRecord record)
    {
        EventId = eventId;
        Price = record.Price;
        Size = record.Size;
        Side = record.Side;
        FeeRateBps = record.FeeRateBps;
        TransactionHash = record.TransactionHash;
    }

    public long EventId { get; private set; }
    public decimal Price { get; private set; }
    public decimal? Size { get; private set; }
    public TradeSide Side { get; private set; }
    public decimal? FeeRateBps { get; private set; }
    public string? TransactionHash { get; private set; }
}
