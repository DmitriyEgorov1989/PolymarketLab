using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Configurations;

internal sealed class NewMarketAssetConfiguration
    : IEntityTypeConfiguration<NewMarketAssetEntity>
{
    public void Configure(EntityTypeBuilder<NewMarketAssetEntity> builder)
    {
        builder.ToTable("new_market_assets", "data_collection");
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

        builder.Property(asset => asset.Outcome)
            .HasColumnName("outcome")
            .HasColumnType("text")
            .IsRequired();

        builder.HasOne<NewMarketEntity>()
            .WithMany()
            .HasForeignKey(asset => asset.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(asset => new { asset.EventId, asset.ItemIndex })
            .IsUnique()
            .HasDatabaseName("ux_new_market_assets_event_id_item_index");
    }
}
