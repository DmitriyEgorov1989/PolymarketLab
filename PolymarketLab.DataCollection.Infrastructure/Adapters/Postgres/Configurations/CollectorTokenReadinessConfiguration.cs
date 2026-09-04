using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Configurations;

internal sealed class CollectorTokenReadinessConfiguration
    : IEntityTypeConfiguration<CollectorTokenReadinessRecord>
{
    public void Configure(EntityTypeBuilder<CollectorTokenReadinessRecord> builder)
    {
        builder.ToTable("collector_token_readiness", "data_collection");
        builder.HasKey(readiness => new
        {
            readiness.SessionId,
            readiness.ConnectionEpoch,
            readiness.TokenId
        });

        builder.Property(readiness => readiness.SessionId)
            .HasColumnName("session_id")
            .HasConversion(
                id => id.Value,
                value => CollectorSessionId.Create(value).Value)
            .ValueGeneratedNever();
        builder.Property(readiness => readiness.ConnectionEpoch)
            .HasColumnName("connection_epoch")
            .IsRequired();
        builder.Property(readiness => readiness.TokenId)
            .HasColumnName("token_id")
            .HasConversion(
                id => id.Value,
                value => TokenId.Create(value).Value)
            .IsRequired();
        builder.Property(readiness => readiness.InitialBookEnqueuedAt)
            .HasColumnName("initial_book_enqueued_at")
            .IsRequired();

        builder.HasOne<CollectorSession>()
            .WithMany()
            .HasForeignKey(readiness => readiness.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_collector_token_readiness_epoch_positive",
            "connection_epoch > 0"));
    }
}
