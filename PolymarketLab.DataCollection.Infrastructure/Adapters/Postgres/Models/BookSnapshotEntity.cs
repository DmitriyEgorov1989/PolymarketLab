using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

internal sealed class BookSnapshotEntity
{
    private BookSnapshotEntity()
    {
    }

    public BookSnapshotEntity(long eventId, BookSnapshotRecord record)
    {
        EventId = eventId;
        Hash = record.Hash;
        TickSize = record.TickSize;
        LastTradePrice = record.LastTradePrice;
    }

    public long EventId { get; private set; }
    public string Hash { get; private set; } = string.Empty;
    public decimal? TickSize { get; private set; }
    public decimal? LastTradePrice { get; private set; }
}
