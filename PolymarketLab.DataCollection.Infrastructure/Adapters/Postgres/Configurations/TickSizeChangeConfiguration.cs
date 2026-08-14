using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Configurations;

internal sealed class TickSizeChangeConfiguration
    : IEntityTypeConfiguration<TickSizeChangeEntity>
{
    public void Configure(EntityTypeBuilder<TickSizeChangeEntity> builder)
    {
        builder.ToTable("tick_size_changes", "data_collection");
        builder.HasKey(change => change.EventId);

        builder.Property(change => change.EventId)
            .HasColumnName("event_id")
            .ValueGeneratedNever();

        builder.Property(change => change.OldTickSize)
            .HasColumnName("old_tick_size")
            .HasPrecision(29, 28)
            .IsRequired();

        builder.Property(change => change.NewTickSize)
            .HasColumnName("new_tick_size")
            .HasPrecision(29, 28)
            .IsRequired();

        builder.HasOne<NormalizedEventRecord>()
            .WithOne()
            .HasForeignKey<TickSizeChangeEntity>(change => change.EventId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
