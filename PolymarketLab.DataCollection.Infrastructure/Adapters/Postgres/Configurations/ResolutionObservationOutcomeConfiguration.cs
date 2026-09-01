using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Configurations;

internal sealed class ResolutionObservationOutcomeConfiguration
    : IEntityTypeConfiguration<ResolutionObservationOutcomeEntity>
{
    public void Configure(EntityTypeBuilder<ResolutionObservationOutcomeEntity> builder)
    {
        builder.ToTable("resolution_observation_outcomes", "data_collection");
        builder.HasKey(outcome => new { outcome.ObservationId, outcome.OutcomeIndex });
        builder.Property(outcome => outcome.ObservationId).HasColumnName("observation_id");
        builder.Property(outcome => outcome.OutcomeIndex).HasColumnName("outcome_index");
        builder.Property(outcome => outcome.TokenId).HasColumnName("token_id").HasColumnType("text").IsRequired();
        builder.Property(outcome => outcome.Outcome).HasColumnName("outcome").HasColumnType("text");
        builder.Property(outcome => outcome.Price).HasColumnName("price").HasPrecision(29, 18);
        builder.Property(outcome => outcome.IsWinner).HasColumnName("is_winner").IsRequired();

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_resolution_observation_outcomes_index_nonnegative",
            "outcome_index >= 0"));
    }
}
