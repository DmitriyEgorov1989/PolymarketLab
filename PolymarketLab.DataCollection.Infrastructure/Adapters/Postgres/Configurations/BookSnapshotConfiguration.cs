using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Configurations;

internal sealed class BookSnapshotConfiguration
    : IEntityTypeConfiguration<BookSnapshotEntity>
{
    public void Configure(EntityTypeBuilder<BookSnapshotEntity> builder)
    {
        builder.ToTable("book_snapshots", "data_collection");
        builder.HasKey(snapshot => snapshot.EventId);

        builder.Property(snapshot => snapshot.EventId)
            .HasColumnName("event_id")
            .ValueGeneratedNever();

        builder.Property(snapshot => snapshot.Hash)
            .HasColumnName("hash")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(snapshot => snapshot.TickSize)
            .HasColumnName("tick_size")
            .HasPrecision(29, 28);

        builder.Property(snapshot => snapshot.LastTradePrice)
            .HasColumnName("last_trade_price")
            .HasPrecision(29, 28);

        builder.HasOne<NormalizedEventRecord>()
            .WithOne()
            .HasForeignKey<BookSnapshotEntity>(snapshot => snapshot.EventId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
