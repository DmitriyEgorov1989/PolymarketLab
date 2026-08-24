namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;

/// <summary>Закрытое объединение нормализованных событий, влияющих на стакан.</summary>
public abstract record NormalizedOrderBookEvent
{
    private NormalizedOrderBookEvent()
    {
    }

    /// <summary>Полный снимок стакана одного актива.</summary>
    public sealed record BookSnapshot : NormalizedOrderBookEvent
    {
        /// <summary>Создаёт событие полного снимка.</summary>
        /// <param name="record">Проверенная модель нормализованного снимка.</param>
        public BookSnapshot(BookSnapshotRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);
            Record = record;
        }

        /// <summary>Проверенная модель нормализованного снимка.</summary>
        public BookSnapshotRecord Record { get; }
    }

    /// <summary>Изменения уровней одного market-level события.</summary>
    public sealed record PriceChanges : NormalizedOrderBookEvent
    {
        /// <summary>Создаёт одно атомарное событие изменения ценовых уровней.</summary>
        /// <param name="records">Непустая группа изменений одного исходного события.</param>
        public PriceChanges(IReadOnlyCollection<PriceChangeRecord> records)
        {
            ArgumentNullException.ThrowIfNull(records);
            if (records.Count == 0)
                throw new ArgumentException("Price change event cannot be empty.", nameof(records));
            if (records.Any(record => record is null))
                throw new ArgumentException("Price change event cannot contain null records.", nameof(records));

            Records = records.ToArray();
        }

        /// <summary>Изменения уровней в порядке исходного события.</summary>
        public IReadOnlyList<PriceChangeRecord> Records { get; }
    }

    /// <summary>Изменение шага цены одного актива.</summary>
    public sealed record TickSizeChange : NormalizedOrderBookEvent
    {
        /// <summary>Создаёт событие изменения шага цены.</summary>
        /// <param name="record">Проверенная модель изменения шага цены.</param>
        public TickSizeChange(TickSizeChangeRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);
            Record = record;
        }

        /// <summary>Проверенная модель изменения шага цены.</summary>
        public TickSizeChangeRecord Record { get; }
    }

    /// <summary>Контрольные лучшие цены и спред одного актива.</summary>
    public sealed record BestBidAsk : NormalizedOrderBookEvent
    {
        /// <summary>Создаёт событие контрольных лучших цен.</summary>
        /// <param name="record">Проверенная модель лучших цен и спреда.</param>
        public BestBidAsk(BestBidAskRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);
            Record = record;
        }

        /// <summary>Проверенная модель лучших цен и спреда.</summary>
        public BestBidAskRecord Record { get; }
    }
}
