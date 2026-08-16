using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Configurations;

internal sealed class RawMessageNormalizationConfiguration
    : IEntityTypeConfiguration<RawMessageNormalizationRecord>
{
    public void Configure(EntityTypeBuilder<RawMessageNormalizationRecord> builder)
    {
        builder.ToTable("raw_message_normalizations", "data_collection");
        builder.HasKey(normalization => new
        {
            normalization.RawMessageId,
            normalization.ProjectionVersion
        });

        builder.Property(normalization => normalization.RawMessageId)
            .HasColumnName("raw_message_id")
            .ValueGeneratedNever();

        builder.Property(normalization => normalization.ProjectionVersion)
            .HasColumnName("projection_version")
            .IsRequired();

        builder.Property(normalization => normalization.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(normalization => normalization.AttemptCount)
            .HasColumnName("attempt_count")
            .IsRequired();

        builder.Property(normalization => normalization.ClaimedAt)
            .HasColumnName("claimed_at");

        builder.Property(normalization => normalization.CompletedAt)
            .HasColumnName("completed_at");

        builder.Property(normalization => normalization.ErrorCode)
            .HasColumnName("error_code")
            .HasMaxLength(200);

        builder.Property(normalization => normalization.ErrorMessage)
            .HasColumnName("error_message")
            .HasMaxLength(2000);

        builder.Property(normalization => normalization.ErrorField)
            .HasColumnName("error_field")
            .HasMaxLength(500);

        builder.HasOne<RawMarketMessageRecord>()
            .WithMany()
            .HasForeignKey(normalization => normalization.RawMessageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(normalization => new
            {
                normalization.ProjectionVersion,
                normalization.Status,
                normalization.RawMessageId
            })
            .HasDatabaseName(
                "ix_raw_message_normalizations_projection_status_raw_message_id");
    }
}
