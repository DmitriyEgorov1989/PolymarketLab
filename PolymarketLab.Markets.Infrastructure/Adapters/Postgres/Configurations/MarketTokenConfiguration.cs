using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolymarketLab.Markets.Core.Domain.Models.Market.Entity;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.Markets.Infrastructure.Adapters.Postgres.Configurations;

internal sealed class MarketTokenConfiguration : IEntityTypeConfiguration<MarketToken>
{
    public void Configure(EntityTypeBuilder<MarketToken> builder)
    {
        builder.ToTable("market_tokens");

        builder.HasKey(token => token.Id);

        builder.Property(token => token.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(token => token.MarketId)
            .HasColumnName("market_id")
            .HasConversion(
                marketId => marketId.Value,
                value => MarketId.Create(value).Value)
            .IsRequired();

        builder.Property(token => token.ExternalTokenId)
            .HasColumnName("external_token_id")
            .HasConversion(
                tokenId => tokenId.Value,
                value => TokenId.Create(value).Value)
            .IsRequired();

        builder.Property(token => token.Outcome)
            .HasColumnName("outcome")
            .IsRequired();

        builder.Property(token => token.OutcomeIndex)
            .HasColumnName("outcome_index")
            .IsRequired();

        builder.HasIndex(token => new { token.MarketId, token.ExternalTokenId })
            .IsUnique()
            .HasDatabaseName("ux_market_tokens_market_id_external_token_id");

        builder.HasIndex(token => new { token.MarketId, token.OutcomeIndex })
            .IsUnique()
            .HasDatabaseName("ux_market_tokens_market_id_outcome_index");
    }
}
