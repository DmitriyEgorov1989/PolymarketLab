using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Configurations;

internal sealed class NormalizedEventConfiguration
    : IEntityTypeConfiguration<NormalizedEventRecord>
{
    public void Configure(EntityTypeBuilder<NormalizedEventRecord> builder)
    {
        builder.ToTable("normalized_events", "data_collection");
        builder.HasKey(normalizedEvent => normalizedEvent.Id);

        builder.Property(normalizedEvent => normalizedEvent.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(normalizedEvent => normalizedEvent.RawMessageId)
            .HasColumnName("raw_message_id")
            .IsRequired();

        builder.Property(normalizedEvent => normalizedEvent.RawItemIndex)
            .HasColumnName("raw_item_index")
            .IsRequired();

        builder.Property(normalizedEvent => normalizedEvent.ProjectionVersion)
            .HasColumnName("projection_version")
            .IsRequired();

        builder.Property(normalizedEvent => normalizedEvent.NormalizerVersion)
            .HasColumnName("normalizer_version")
            .IsRequired();

        builder.Property(normalizedEvent => normalizedEvent.EventType)
            .HasColumnName("event_type")
            .IsRequired();

        builder.Property(normalizedEvent => normalizedEvent.SessionId)
            .HasColumnName("session_id")
            .HasConversion(
                id => id.Value,
                value => CollectorSessionId.Create(value).Value)
            .IsRequired();

        builder.Property(normalizedEvent => normalizedEvent.ReceivedAt)
            .HasColumnName("received_at")
            .IsRequired();

        builder.Property(normalizedEvent => normalizedEvent.SourceTimestamp)
            .HasColumnName("source_timestamp");

        builder.Property(normalizedEvent => normalizedEvent.MarketConditionId)
            .HasColumnName("market_condition_id");

        builder.Property(normalizedEvent => normalizedEvent.AssetId)
            .HasColumnName("asset_id");

        builder.Property(normalizedEvent => normalizedEvent.NormalizedAt)
            .HasColumnName("normalized_at")
            .IsRequired();

        builder.HasOne<RawMarketMessageRecord>()
            .WithMany()
            .HasForeignKey(normalizedEvent => normalizedEvent.RawMessageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(normalizedEvent => new
            {
                normalizedEvent.RawMessageId,
                normalizedEvent.RawItemIndex,
                normalizedEvent.ProjectionVersion
            })
            .IsUnique()
            .HasDatabaseName("ux_normalized_events_raw_message_item_projection");

        builder.HasIndex(normalizedEvent => new
            {
                normalizedEvent.ProjectionVersion,
                normalizedEvent.EventType,
                normalizedEvent.RawMessageId
            })
            .HasDatabaseName("ix_normalized_events_projection_event_raw_message");
    }
}
