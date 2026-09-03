using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Configurations;

internal sealed class CollectorDatasetCleanupAuditConfiguration
    : IEntityTypeConfiguration<CollectorDatasetCleanupAuditRecord>
{
    public void Configure(EntityTypeBuilder<CollectorDatasetCleanupAuditRecord> builder)
    {
        builder.ToTable("collector_dataset_cleanup_audits", "data_collection");
        builder.HasKey(audit => audit.SessionId);

        builder.Property(audit => audit.SessionId)
            .HasColumnName("session_id")
            .HasConversion(
                id => id.Value,
                value => CollectorSessionId.Create(value).Value)
            .ValueGeneratedNever();
        builder.Property(audit => audit.CompletedAt)
            .HasColumnName("completed_at")
            .IsRequired();
        builder.Property(audit => audit.DeletedRawMessageCount)
            .HasColumnName("deleted_raw_message_count")
            .IsRequired();
        builder.Property(audit => audit.DeletedNormalizationCount)
            .HasColumnName("deleted_normalization_count")
            .IsRequired();
        builder.Property(audit => audit.DeletedNormalizedEventCount)
            .HasColumnName("deleted_normalized_event_count")
            .IsRequired();

        builder.HasOne<CollectorSession>()
            .WithOne()
            .HasForeignKey<CollectorDatasetCleanupAuditRecord>(audit => audit.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_collector_dataset_cleanup_audits_counts_nonnegative",
            "deleted_raw_message_count >= 0 AND deleted_normalization_count >= 0 AND deleted_normalized_event_count >= 0"));
    }
}
