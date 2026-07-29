using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.CollectorSession;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Configurations
{
    internal sealed class CollectorSessionConfiguration
    : IEntityTypeConfiguration<CollectorSession>
    {
        public void Configure(EntityTypeBuilder<CollectorSession> builder)
        {
            builder.ToTable("collector_sessions", "data_collection");
            builder.HasKey(x => x.Id);

            builder.Property(x => x.Id)
                .HasColumnName("id")
                .HasConversion(
                    id => id.Value,
                    value => CollectorSessionId.Create(value).Value)
                .ValueGeneratedNever();

            builder.Property(x => x.MarketId)
                .HasColumnName("market_id")
                .HasConversion(
                    id => id.Value,
                    value => MarketId.Create(value).Value)
                .IsRequired();

            builder.Property(x => x.Status)
                .HasColumnName("status")
                .HasConversion<int>()
                .IsConcurrencyToken()
                .IsRequired();

            builder.Property(x => x.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            builder.Property(x => x.StartedAt)
                .HasColumnName("started_at");

            builder.Property(x => x.StoppedAt)
                .HasColumnName("stopped_at");

            builder.Property(x => x.StopReason)
                .HasColumnName("stop_reason")
                .HasConversion<int?>();

            builder.Property(x => x.FailureCode)
                .HasColumnName("failure_code")
                .HasMaxLength(200);

            builder.Property(x => x.FailureMessage)
                .HasColumnName("failure_message")
                .HasMaxLength(2000);

            builder.HasIndex(x => x.MarketId)
                .IsUnique()
                .HasFilter(CollectorSessionDatabaseConstraints.ActiveStatusFilter)
                .HasDatabaseName(CollectorSessionDatabaseConstraints.ActiveMarket);
        }
    }
}
