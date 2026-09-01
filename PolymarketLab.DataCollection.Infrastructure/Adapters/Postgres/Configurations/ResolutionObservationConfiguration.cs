using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Configurations;

internal sealed class ResolutionObservationConfiguration
    : IEntityTypeConfiguration<ResolutionObservationEntity>
{
    public void Configure(EntityTypeBuilder<ResolutionObservationEntity> builder)
    {
        builder.ToTable("resolution_observations", "data_collection");
        builder.HasKey(observation => observation.Id);
        builder.Property(observation => observation.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();
        builder.Property(observation => observation.SessionId)
            .HasColumnName("session_id")
            .HasConversion(
                id => id.Value,
                value => CollectorSessionId.Create(value).Value)
            .IsRequired();
        builder.Property(observation => observation.Source)
            .HasColumnName("source")
            .HasConversion<int>()
            .IsRequired();
        builder.Property(observation => observation.ObservedAt)
            .HasColumnName("observed_at")
            .IsRequired();
        builder.Property(observation => observation.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();
        builder.Property(observation => observation.WinnerTokenId).HasColumnName("winner_token_id").HasColumnType("text");
        builder.Property(observation => observation.WinnerOutcome).HasColumnName("winner_outcome").HasColumnType("text");
        builder.Property(observation => observation.ExternalEventId).HasColumnName("external_event_id").HasColumnType("text");
        builder.Property(observation => observation.EventSlug).HasColumnName("event_slug").HasColumnType("text");
        builder.Property(observation => observation.ExternalMarketId).HasColumnName("external_market_id").HasColumnType("text");
        builder.Property(observation => observation.MarketSlug).HasColumnName("market_slug").HasColumnType("text");
        builder.Property(observation => observation.ConditionId).HasColumnName("condition_id").HasColumnType("text");
        builder.Property(observation => observation.Closed).HasColumnName("closed");
        builder.Property(observation => observation.AcceptingOrders).HasColumnName("accepting_orders");
        builder.Property(observation => observation.UmaResolutionStatus).HasColumnName("uma_resolution_status").HasColumnType("text");
        builder.Property(observation => observation.ExternalClosedAt).HasColumnName("external_closed_at");
        builder.Property(observation => observation.ErrorCode).HasColumnName("error_code").HasColumnType("text");
        builder.Property(observation => observation.ErrorMessage).HasColumnName("error_message").HasColumnType("text");
        builder.Property(observation => observation.RawMessageId).HasColumnName("raw_message_id");
        builder.Property(observation => observation.RawItemIndex).HasColumnName("raw_item_index");
        builder.Property(observation => observation.ConnectionEpoch).HasColumnName("connection_epoch");

        builder.HasOne<CollectorSession>()
            .WithMany()
            .HasForeignKey(observation => observation.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(observation => observation.Outcomes)
            .WithOne()
            .HasForeignKey(outcome => outcome.ObservationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(observation => new
            {
                observation.RawMessageId,
                observation.RawItemIndex
            })
            .IsUnique()
            .HasFilter("raw_message_id IS NOT NULL")
            .HasDatabaseName("ux_resolution_observations_ws_raw_item");
        builder.HasIndex(observation => new
            {
                observation.SessionId,
                observation.ObservedAt,
                observation.Id
            })
            .HasDatabaseName("ix_resolution_observations_session_observed_id");
    }
}
