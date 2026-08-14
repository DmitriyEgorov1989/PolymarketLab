using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

internal sealed class BestBidAskEntity
{
    private BestBidAskEntity()
    {
    }

    public BestBidAskEntity(long eventId, BestBidAskRecord record)
    {
        EventId = eventId;
        BestBid = record.BestBid;
        BestAsk = record.BestAsk;
        Spread = record.Spread;
    }

    public long EventId { get; private set; }
    public decimal BestBid { get; private set; }
    public decimal BestAsk { get; private set; }
    public decimal Spread { get; private set; }
}
