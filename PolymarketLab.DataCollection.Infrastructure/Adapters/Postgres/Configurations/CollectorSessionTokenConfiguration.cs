using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Configurations;

internal sealed class CollectorSessionTokenConfiguration
    : IEntityTypeConfiguration<CollectorSessionToken>
{
    public void Configure(EntityTypeBuilder<CollectorSessionToken> builder)
    {
        builder.ToTable("collector_session_tokens", "data_collection");
        builder.HasKey(token => new { token.SessionId, token.OutcomeIndex });

        builder.Property(token => token.SessionId)
            .HasColumnName("session_id")
            .HasConversion(
                id => id.Value,
                value => CollectorSessionId.Create(value).Value)
            .ValueGeneratedNever();
        builder.Property(token => token.TokenId)
            .HasColumnName("token_id")
            .HasConversion(
                id => id.Value,
                value => TokenId.Create(value).Value)
            .IsRequired();
        builder.Property(token => token.Outcome)
            .HasColumnName("outcome")
            .IsRequired();
        builder.Property(token => token.OutcomeIndex)
            .HasColumnName("outcome_index")
            .ValueGeneratedNever();

        builder.HasIndex(token => new { token.SessionId, token.TokenId })
            .IsUnique()
            .HasDatabaseName("ux_collector_session_tokens_session_token");
    }
}
