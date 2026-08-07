using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Configurations;

internal sealed class CollectorSessionProgressConfiguration
    : IEntityTypeConfiguration<CollectorSessionProgressRecord>
{
    public void Configure(EntityTypeBuilder<CollectorSessionProgressRecord> builder)
    {
        builder.ToTable("collector_session_progress", "data_collection");
        builder.HasKey(progress => progress.SessionId);

        builder.Property(progress => progress.SessionId)
            .HasColumnName("session_id")
            .HasConversion(
                id => id.Value,
                value => CollectorSessionId.Create(value).Value)
            .ValueGeneratedNever();

        builder.Property(progress => progress.MessagesReceived)
            .HasColumnName("messages_received")
            .HasDefaultValue(0L)
            .IsRequired();

        builder.Property(progress => progress.MessagesPersisted)
            .HasColumnName("messages_persisted")
            .HasDefaultValue(0L)
            .IsRequired();

        builder.Property(progress => progress.LastMessageAt)
            .HasColumnName("last_message_at");

        builder.Property(progress => progress.ReconnectCount)
            .HasColumnName("reconnect_count")
            .HasDefaultValue(0L)
            .IsRequired();

        builder.HasOne<CollectorSession>()
            .WithOne()
            .HasForeignKey<CollectorSessionProgressRecord>(progress => progress.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
