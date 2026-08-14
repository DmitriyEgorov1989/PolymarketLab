using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Configurations;

internal sealed class NewMarketConfiguration
    : IEntityTypeConfiguration<NewMarketEntity>
{
    public void Configure(EntityTypeBuilder<NewMarketEntity> builder)
    {
        builder.ToTable("new_markets", "data_collection");
        builder.HasKey(market => market.EventId);

        builder.Property(market => market.EventId)
            .HasColumnName("event_id")
            .ValueGeneratedNever();

        builder.Property(market => market.ExternalMarketId)
            .HasColumnName("external_market_id")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(market => market.Question)
            .HasColumnName("question")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(market => market.Slug)
            .HasColumnName("slug")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(market => market.Description)
            .HasColumnName("description")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(market => market.Active)
            .HasColumnName("active")
            .IsRequired();

        builder.Property(market => market.SportsMarketType)
            .HasColumnName("sports_market_type")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(market => market.Line)
            .HasColumnName("line")
            .HasPrecision(29, 18);

        builder.Property(market => market.GameStartTime)
            .HasColumnName("game_start_time")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(market => market.OrderPriceMinTickSize)
            .HasColumnName("order_price_min_tick_size")
            .HasPrecision(29, 28)
            .IsRequired();

        builder.Property(market => market.GroupItemTitle)
            .HasColumnName("group_item_title")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(market => market.TakerBaseFee)
            .HasColumnName("taker_base_fee")
            .HasPrecision(29, 18)
            .IsRequired();

        builder.Property(market => market.FeesEnabled)
            .HasColumnName("fees_enabled")
            .IsRequired();

        builder.Property(market => market.EventMessageId)
            .HasColumnName("event_message_id")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(market => market.EventMessageTicker)
            .HasColumnName("event_message_ticker")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(market => market.EventMessageSlug)
            .HasColumnName("event_message_slug")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(market => market.EventMessageTitle)
            .HasColumnName("event_message_title")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(market => market.EventMessageDescription)
            .HasColumnName("event_message_description")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(market => market.FeeScheduleExponent)
            .HasColumnName("fee_schedule_exponent")
            .HasPrecision(29, 18)
            .IsRequired();

        builder.Property(market => market.FeeScheduleRate)
            .HasColumnName("fee_schedule_rate")
            .HasPrecision(29, 18)
            .IsRequired();

        builder.Property(market => market.FeeScheduleRebateRate)
            .HasColumnName("fee_schedule_rebate_rate")
            .HasPrecision(29, 18)
            .IsRequired();

        builder.Property(market => market.FeeScheduleTakerOnly)
            .HasColumnName("fee_schedule_taker_only")
            .IsRequired();

        builder.HasOne<NormalizedEventRecord>()
            .WithOne()
            .HasForeignKey<NewMarketEntity>(market => market.EventId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
