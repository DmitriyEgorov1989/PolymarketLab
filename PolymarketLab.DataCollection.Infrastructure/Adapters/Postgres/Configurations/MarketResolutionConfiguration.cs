using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Configurations;

internal sealed class MarketResolutionConfiguration
    : IEntityTypeConfiguration<MarketResolutionEntity>
{
    public void Configure(EntityTypeBuilder<MarketResolutionEntity> builder)
    {
        builder.ToTable("market_resolutions", "data_collection");
        builder.HasKey(resolution => resolution.EventId);

        builder.Property(resolution => resolution.EventId)
            .HasColumnName("event_id")
            .ValueGeneratedNever();

        builder.Property(resolution => resolution.ExternalMarketId)
            .HasColumnName("external_market_id")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(resolution => resolution.WinningAssetId)
            .HasColumnName("winning_asset_id")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(resolution => resolution.WinningOutcome)
            .HasColumnName("winning_outcome")
            .HasColumnType("text")
            .IsRequired();

        builder.HasOne<NormalizedEventRecord>()
            .WithOne()
            .HasForeignKey<MarketResolutionEntity>(resolution => resolution.EventId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
