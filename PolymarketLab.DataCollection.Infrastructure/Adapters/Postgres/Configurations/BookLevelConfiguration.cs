using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Configurations;

internal sealed class BookLevelConfiguration
    : IEntityTypeConfiguration<BookLevelEntity>
{
    public void Configure(EntityTypeBuilder<BookLevelEntity> builder)
    {
        builder.ToTable("book_levels", "data_collection");
        builder.HasKey(level => level.Id);

        builder.Property(level => level.Id)
            .HasColumnName("id")
            .HasColumnType("bigint")
            .ValueGeneratedOnAdd();

        builder.Property(level => level.EventId)
            .HasColumnName("event_id")
            .IsRequired();

        builder.Property(level => level.Side)
            .HasColumnName("side")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(level => level.LevelIndex)
            .HasColumnName("level_index")
            .IsRequired();

        builder.Property(level => level.Price)
            .HasColumnName("price")
            .HasPrecision(29, 28)
            .IsRequired();

        builder.Property(level => level.Size)
            .HasColumnName("size")
            .HasPrecision(29, 18)
            .IsRequired();

        builder.HasOne<BookSnapshotEntity>()
            .WithMany()
            .HasForeignKey(level => level.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(level => new
            {
                level.EventId,
                level.Side,
                level.LevelIndex
            })
            .IsUnique()
            .HasDatabaseName("ux_book_levels_event_side_level_index");
    }
}
