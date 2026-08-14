using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

internal sealed class TickSizeChangeEntity
{
    private TickSizeChangeEntity()
    {
    }

    public TickSizeChangeEntity(long eventId, TickSizeChangeRecord record)
    {
        EventId = eventId;
        OldTickSize = record.OldTickSize;
        NewTickSize = record.NewTickSize;
    }

    public long EventId { get; private set; }
    public decimal OldTickSize { get; private set; }
    public decimal NewTickSize { get; private set; }
}
