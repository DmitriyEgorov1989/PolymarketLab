using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Configurations;

internal sealed class LastTradePriceConfiguration
    : IEntityTypeConfiguration<LastTradePriceEntity>
{
    public void Configure(EntityTypeBuilder<LastTradePriceEntity> builder)
    {
        builder.ToTable("last_trade_price", "data_collection");
        builder.HasKey(lastTradePrice => lastTradePrice.EventId);

        builder.Property(lastTradePrice => lastTradePrice.EventId)
            .HasColumnName("event_id")
            .ValueGeneratedNever();

        builder.Property(lastTradePrice => lastTradePrice.Price)
            .HasColumnName("price")
            .HasPrecision(29, 28)
            .IsRequired();

        builder.Property(lastTradePrice => lastTradePrice.Size)
            .HasColumnName("size")
            .HasPrecision(29, 18);

        builder.Property(lastTradePrice => lastTradePrice.Side)
            .HasColumnName("side")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(lastTradePrice => lastTradePrice.FeeRateBps)
            .HasColumnName("fee_rate_bps")
            .HasPrecision(29, 18);

        builder.Property(lastTradePrice => lastTradePrice.TransactionHash)
            .HasColumnName("transaction_hash")
            .HasColumnType("text");

        builder.HasOne<NormalizedEventRecord>()
            .WithOne()
            .HasForeignKey<LastTradePriceEntity>(lastTradePrice => lastTradePrice.EventId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
