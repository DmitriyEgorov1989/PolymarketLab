using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Configurations;

internal sealed class BestBidAskConfiguration
    : IEntityTypeConfiguration<BestBidAskEntity>
{
    public void Configure(EntityTypeBuilder<BestBidAskEntity> builder)
    {
        builder.ToTable("best_bid_asks", "data_collection");
        builder.HasKey(quote => quote.EventId);

        builder.Property(quote => quote.EventId)
            .HasColumnName("event_id")
            .ValueGeneratedNever();

        builder.Property(quote => quote.BestBid)
            .HasColumnName("best_bid")
            .HasPrecision(29, 28)
            .IsRequired();

        builder.Property(quote => quote.BestAsk)
            .HasColumnName("best_ask")
            .HasPrecision(29, 28)
            .IsRequired();

        builder.Property(quote => quote.Spread)
            .HasColumnName("spread")
            .HasPrecision(29, 18)
            .IsRequired();

        builder.HasOne<NormalizedEventRecord>()
            .WithOne()
            .HasForeignKey<BestBidAskEntity>(quote => quote.EventId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
