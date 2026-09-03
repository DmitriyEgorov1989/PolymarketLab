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
            builder.ToTable(
                "collector_sessions",
                "data_collection",
                table => table.HasCheckConstraint(
                    CollectorSessionDatabaseConstraints.ExclusiveSlotCheck,
                    "\"exclusive_slot\" = 1"));
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

            builder.Property(x => x.ExternalEventId)
                .HasColumnName("external_event_id");
            builder.Property(x => x.EventSlug)
                .HasColumnName("event_slug");
            builder.Property(x => x.ExternalMarketId)
                .HasColumnName("external_market_id");
            builder.Property(x => x.MarketSlug)
                .HasColumnName("market_slug");
            builder.Property(x => x.ConditionId)
                .HasColumnName("condition_id");
            builder.Property(x => x.EventStartsAt)
                .HasColumnName("event_starts_at");
            builder.Property(x => x.EventEndsAt)
                .HasColumnName("event_ends_at");
            builder.Property(x => x.ProjectionVersion)
                .HasColumnName("projection_version");

            builder.Property(x => x.Status)
                .HasColumnName("status")
                .HasConversion<int>()
                .IsConcurrencyToken()
                .IsRequired();

            builder.Property(x => x.Phase)
                .HasColumnName("phase")
                .HasConversion<int?>();

            builder.Property(x => x.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            builder.Property(x => x.StartedAt)
                .HasColumnName("started_at");

            builder.Property(x => x.SubscriptionReadyAt)
                .HasColumnName("subscription_ready_at");

            builder.Property(x => x.ResolutionSignaledAt)
                .HasColumnName("resolution_signaled_at");

            builder.Property(x => x.ResolutionConfirmedAt)
                .HasColumnName("resolution_confirmed_at");

            builder.Property(x => x.AwaitingNormalizationAt)
                .HasColumnName("awaiting_normalization_at");

            builder.Property(x => x.WinningTokenId)
                .HasColumnName("winning_token_id")
                .HasMaxLength(500);

            builder.Property(x => x.WinningOutcome)
                .HasColumnName("winning_outcome")
                .HasMaxLength(500);

            builder.Property(x => x.ResolutionConnectionEpoch)
                .HasColumnName("resolution_connection_epoch");

            builder.Property(x => x.StoppedAt)
                .HasColumnName("stopped_at");

            builder.Property(x => x.InvalidatingAt)
                .HasColumnName("invalidating_at");

            builder.Property(x => x.StopReason)
                .HasColumnName("stop_reason")
                .HasConversion<int?>();

            builder.Property(x => x.FailureCode)
                .HasColumnName("failure_code")
                .HasMaxLength(200);

            builder.Property(x => x.FailureMessage)
                .HasColumnName("failure_message")
                .HasMaxLength(2000);

            builder.Property<short>(CollectorSessionDatabaseConstraints.ExclusiveSlotProperty)
                .HasColumnName("exclusive_slot")
                .HasDefaultValue((short)1)
                .IsRequired();

            builder.HasIndex(CollectorSessionDatabaseConstraints.ExclusiveSlotProperty)
                .IsUnique()
                .HasFilter(CollectorSessionDatabaseConstraints.ExclusiveStatusFilter)
                .HasDatabaseName(CollectorSessionDatabaseConstraints.ExclusiveSlot);

            builder.HasMany(x => x.Tokens)
                .WithOne()
                .HasForeignKey(token => token.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.Navigation(x => x.Tokens)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        }
    }
}
