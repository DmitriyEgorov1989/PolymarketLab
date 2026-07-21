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
            .HasConversion(
                marketId => marketId.Value,
                value => MarketId.Create(value).Value)
            .ValueGeneratedNever();

        builder.Property(market => market.ExternalId)
            .HasColumnName("external_market_id")
            .HasConversion(
                externalId => externalId.Value,
                value => ExternalMarketId.Create(value).Value)
            .IsRequired();

        builder.Property(market => market.Slug)
            .HasColumnName("slug")
            .HasConversion(
                slug => slug.Value,
                value => MarketSlug.Create(value).Value)
            .IsRequired();

        builder.Property(market => market.ConditionId)
            .HasColumnName("condition_id")
            .HasConversion(
                conditionId => conditionId.Value,
                value => ConditionId.Create(value).Value)
            .IsRequired();

        builder.Property(market => market.Question)
            .HasColumnName("question")
            .IsRequired();

        builder.Property(market => market.StartsAt)
            .HasColumnName("starts_at");

        builder.Property(market => market.EndsAt)
            .HasColumnName("ends_at");

        builder.HasIndex(market => market.Slug)
            .IsUnique()
            .HasDatabaseName(MarketDatabaseConstraints.Slug);

        builder.HasIndex(market => market.ExternalId)
            .IsUnique()
            .HasDatabaseName(MarketDatabaseConstraints.ExternalId);

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
