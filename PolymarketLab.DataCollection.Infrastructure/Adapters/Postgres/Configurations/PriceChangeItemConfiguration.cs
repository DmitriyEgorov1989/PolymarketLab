using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Configurations;

internal sealed class PriceChangeItemConfiguration
    : IEntityTypeConfiguration<PriceChangeItemEntity>
{
    public void Configure(EntityTypeBuilder<PriceChangeItemEntity> builder)
    {
        builder.ToTable("price_change", "data_collection");
        builder.HasKey(priceChange => priceChange.Id);

        builder.Property(priceChange => priceChange.Id)
            .HasColumnName("id")
            .HasColumnType("bigint")
            .ValueGeneratedOnAdd();

        builder.Property(priceChange => priceChange.EventId)
            .HasColumnName("event_id")
            .IsRequired();

        builder.Property(priceChange => priceChange.ItemIndex)
            .HasColumnName("item_index")
            .IsRequired();

        builder.Property(priceChange => priceChange.AssetId)
            .HasColumnName("asset_id")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(priceChange => priceChange.SourceTimestamp)
            .HasColumnName("source_timestamp");

        builder.Property(priceChange => priceChange.Price)
            .HasColumnName("price")
            .HasPrecision(29, 28)
            .IsRequired();

        builder.Property(priceChange => priceChange.Size)
            .HasColumnName("size")
            .HasPrecision(29, 18)
            .IsRequired();

        builder.Property(priceChange => priceChange.Side)
            .HasColumnName("side")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(priceChange => priceChange.Hash)
            .HasColumnName("hash")
            .HasColumnType("text");

        builder.Property(priceChange => priceChange.BestBid)
            .HasColumnName("best_bid")
            .HasPrecision(29, 28);

        builder.Property(priceChange => priceChange.BestAsk)
            .HasColumnName("best_ask")
            .HasPrecision(29, 28);

        builder.HasOne<NormalizedEventRecord>()
            .WithMany()
            .HasForeignKey(priceChange => priceChange.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(priceChange => new
            {
                priceChange.EventId,
                priceChange.ItemIndex
            })
            .IsUnique()
            .HasDatabaseName("ux_price_change_event_id_item_index");

        builder.HasIndex(priceChange => new
            {
                priceChange.AssetId,
                priceChange.SourceTimestamp
            })
            .HasDatabaseName("ix_price_change_asset_id_source_timestamp");
    }
}
