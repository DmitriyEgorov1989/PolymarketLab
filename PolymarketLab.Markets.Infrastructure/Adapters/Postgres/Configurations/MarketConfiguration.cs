using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolymarketLab.Markets.Core.Domain.Models.Market.MarketAggregate;
using PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.Markets.Infrastructure.Adapters.Postgres.Configurations;

internal sealed class MarketConfiguration : IEntityTypeConfiguration<Market>
{
    public void Configure(EntityTypeBuilder<Market> builder)
    {
        builder.ToTable("markets");

        builder.HasKey(market => market.Id);

        builder.Property(market => market.Id)
            .HasColumnName("id")
            .HasConversion(marketId => marketId.Value, value => MarketId.Create(value).Value)
            .ValueGeneratedNever();

        builder.Property(market => market.ExternalEventId)
            .HasColumnName("external_event_id")
            .HasConversion(id => id.Value, value => ExternalEventId.Create(value).Value)
            .IsRequired();

        builder.Property(market => market.EventSlug)
            .HasColumnName("event_slug")
            .HasConversion(slug => slug.Value, value => EventSlug.Create(value).Value)
            .IsRequired();

        builder.Property(market => market.ExternalMarketId)
            .HasColumnName("external_market_id")
            .HasConversion(id => id.Value, value => ExternalMarketId.Create(value).Value)
            .IsRequired();

        builder.Property(market => market.MarketSlug)
            .HasColumnName("market_slug")
            .HasConversion(slug => slug.Value, value => MarketSlug.Create(value).Value)
            .IsRequired();

        builder.Property(market => market.ConditionId)
            .HasColumnName("condition_id")
            .HasConversion(conditionId => conditionId.Value, value => ConditionId.Create(value).Value)
            .IsRequired();

        builder.Property(market => market.Question)
            .HasColumnName("question")
            .IsRequired();

        builder.Property(market => market.DiscoveredAt)
            .HasColumnName("discovered_at")
            .IsRequired();
        builder.Property(market => market.ExternalCreatedAt).HasColumnName("external_created_at");
        builder.Property(market => market.OrdersOpenedAt).HasColumnName("orders_opened_at");
        builder.Property(market => market.GammaStartDate).HasColumnName("gamma_start_date");
        builder.Property(market => market.EventStartsAt)
            .HasColumnName("event_starts_at")
            .IsRequired();
        builder.Property(market => market.EventEndsAt)
            .HasColumnName("event_ends_at")
            .IsRequired();
        builder.Property(market => market.ExternalClosedAt).HasColumnName("external_closed_at");
        builder.Property(market => market.ScheduleRefreshedAt)
            .HasColumnName("schedule_refreshed_at")
            .IsRequired();

        builder.HasIndex(market => market.ExternalEventId)
            .IsUnique()
            .HasDatabaseName(MarketDatabaseConstraints.ExternalEventId);
        builder.HasIndex(market => market.EventSlug)
            .IsUnique()
            .HasDatabaseName(MarketDatabaseConstraints.EventSlug);
        builder.HasIndex(market => market.MarketSlug)
            .IsUnique()
            .HasDatabaseName(MarketDatabaseConstraints.MarketSlug);
        builder.HasIndex(market => market.ExternalMarketId)
            .IsUnique()
            .HasDatabaseName(MarketDatabaseConstraints.ExternalMarketId);
        builder.HasIndex(market => market.ConditionId)
            .IsUnique()
            .HasDatabaseName(MarketDatabaseConstraints.ConditionId);

        builder.HasMany(market => market.Tokens)
            .WithOne()
            .HasForeignKey(token => token.MarketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(market => market.Tokens)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
