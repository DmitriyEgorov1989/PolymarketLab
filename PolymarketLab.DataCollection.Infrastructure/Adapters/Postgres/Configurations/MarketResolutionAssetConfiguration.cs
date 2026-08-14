using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Configurations;

internal sealed class MarketResolutionAssetConfiguration
    : IEntityTypeConfiguration<MarketResolutionAssetEntity>
{
    public void Configure(EntityTypeBuilder<MarketResolutionAssetEntity> builder)
    {
        builder.ToTable("market_resolution_assets", "data_collection");
        builder.HasKey(asset => asset.Id);

        builder.Property(asset => asset.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(asset => asset.EventId)
            .HasColumnName("event_id")
            .IsRequired();

        builder.Property(asset => asset.ItemIndex)
            .HasColumnName("item_index")
            .IsRequired();

        builder.Property(asset => asset.AssetId)
            .HasColumnName("asset_id")
            .HasColumnType("text")
            .IsRequired();

        builder.HasOne<MarketResolutionEntity>()
            .WithMany()
            .HasForeignKey(asset => asset.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(asset => new { asset.EventId, asset.ItemIndex })
            .IsUnique()
            .HasDatabaseName("ux_market_resolution_assets_event_id_item_index");
    }
}
