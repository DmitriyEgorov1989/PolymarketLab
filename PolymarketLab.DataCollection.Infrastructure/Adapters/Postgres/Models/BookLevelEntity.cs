using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

internal sealed class BookLevelEntity
{
    private BookLevelEntity()
    {
    }

    public BookLevelEntity(long eventId, BookLevelRecord record)
    {
        EventId = eventId;
        Side = record.Side;
        LevelIndex = record.LevelIndex;
        Price = record.Price;
        Size = record.Size;
    }

    public long Id { get; private set; }
    public long EventId { get; private set; }
    public OrderBookSide Side { get; private set; }
    public int LevelIndex { get; private set; }
    public decimal Price { get; private set; }
    public decimal Size { get; private set; }
}
