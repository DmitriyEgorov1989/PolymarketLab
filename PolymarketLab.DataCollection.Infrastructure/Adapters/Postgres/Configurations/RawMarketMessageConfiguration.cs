using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Configurations;

internal sealed class RawMarketMessageConfiguration
    : IEntityTypeConfiguration<RawMarketMessageRecord>
{
    public void Configure(EntityTypeBuilder<RawMarketMessageRecord> builder)
    {
        builder.ToTable("raw_market_messages", "data_collection");
        builder.HasKey(message => message.Id);

        builder.Property(message => message.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(message => message.SessionId)
            .HasColumnName("session_id")
            .HasConversion(
                id => id.Value,
                value => CollectorSessionId.Create(value).Value)
            .IsRequired();

        builder.Property(message => message.ConnectionEpoch)
            .HasColumnName("connection_epoch")
            .IsRequired();

        builder.Property(message => message.ReceivedAt)
            .HasColumnName("received_at")
            .IsRequired();

        builder.Property(message => message.Payload)
            .HasColumnName("payload")
            .HasColumnType("bytea")
            .IsRequired();

        builder.HasOne<CollectorSession>()
            .WithMany()
            .HasForeignKey(message => message.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(message => new
            {
                message.SessionId,
                message.ReceivedAt,
                message.Id
            })
            .HasDatabaseName("ix_raw_market_messages_session_received_id");

        builder.HasIndex(message => new { message.SessionId, message.Id })
            .HasDatabaseName("ix_raw_market_messages_session_id");

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_raw_market_messages_connection_epoch_positive",
            "connection_epoch > 0"));
    }
}
