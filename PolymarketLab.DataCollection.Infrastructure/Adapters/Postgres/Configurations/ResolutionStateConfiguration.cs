using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Configurations;

internal sealed class ResolutionStateConfiguration
    : IEntityTypeConfiguration<ResolutionStateEntity>
{
    public void Configure(EntityTypeBuilder<ResolutionStateEntity> builder)
    {
        builder.ToTable("resolution_states", "data_collection");
        builder.HasKey(state => state.SessionId);

        builder.Property(state => state.SessionId)
            .HasColumnName("session_id")
            .HasConversion(
                id => id.Value,
                value => CollectorSessionId.Create(value).Value)
            .ValueGeneratedNever();
        builder.Property(state => state.LastScannedRawMessageId)
            .HasColumnName("last_scanned_raw_message_id")
            .HasDefaultValue(0L)
            .IsRequired();
        builder.Property(state => state.LastPollingCycleAt)
            .HasColumnName("last_polling_cycle_at");
        builder.Property(state => state.PrimaryObservationId)
            .HasColumnName("primary_observation_id");
        builder.Property(state => state.ConfirmingObservationId)
            .HasColumnName("confirming_observation_id");
        builder.Property(state => state.ConfirmedAt)
            .HasColumnName("confirmed_at");

        builder.HasOne<CollectorSession>()
            .WithOne()
            .HasForeignKey<ResolutionStateEntity>(state => state.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ResolutionObservationEntity>()
            .WithMany()
            .HasForeignKey(state => state.PrimaryObservationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ResolutionObservationEntity>()
            .WithMany()
            .HasForeignKey(state => state.ConfirmingObservationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_resolution_states_cursor_nonnegative",
            "last_scanned_raw_message_id >= 0"));
    }
}
