using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Configurations;

internal sealed class NormalizationSessionCursorConfiguration
    : IEntityTypeConfiguration<NormalizationSessionCursorRecord>
{
    public void Configure(EntityTypeBuilder<NormalizationSessionCursorRecord> builder)
    {
        builder.ToTable("normalization_session_cursors", "data_collection");
        builder.HasKey(cursor => new { cursor.SessionId, cursor.ProjectionVersion });

        builder.Property(cursor => cursor.SessionId)
            .HasColumnName("session_id")
            .HasConversion(
                id => id.Value,
                value => CollectorSessionId.Create(value).Value)
            .ValueGeneratedNever();

        builder.Property(cursor => cursor.ProjectionVersion)
            .HasColumnName("projection_version")
            .IsRequired();

        builder.Property(cursor => cursor.LastClaimedRawMessageId)
            .HasColumnName("last_claimed_raw_message_id")
            .IsRequired();

        builder.HasOne<CollectorSession>()
            .WithMany()
            .HasForeignKey(cursor => cursor.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_normalization_session_cursors_projection_version_positive",
                "projection_version > 0");
            table.HasCheckConstraint(
                "ck_normalization_session_cursors_raw_message_id_positive",
                "last_claimed_raw_message_id > 0");
        });
    }
}
