using Microsoft.EntityFrameworkCore;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres
{
    public sealed class DataCollectionDbContext(DbContextOptions<DataCollectionDbContext> options) : DbContext(options)
    {
        public DbSet<CollectorSession> CollectorSessions => Set<CollectorSession>();
        internal DbSet<CollectorSessionToken> CollectorSessionTokens =>
            Set<CollectorSessionToken>();
        internal DbSet<RawMarketMessageRecord> RawMarketMessages =>
            Set<RawMarketMessageRecord>();
        internal DbSet<CollectorSessionProgressRecord> CollectorSessionProgress =>
            Set<CollectorSessionProgressRecord>();
        internal DbSet<CollectorDatasetCleanupAuditRecord> CollectorDatasetCleanupAudits =>
            Set<CollectorDatasetCleanupAuditRecord>();
        internal DbSet<CollectorTokenReadinessRecord> CollectorTokenReadiness =>
            Set<CollectorTokenReadinessRecord>();
        internal DbSet<RawMessageNormalizationRecord> RawMessageNormalizations =>
            Set<RawMessageNormalizationRecord>();
        internal DbSet<NormalizedEventRecord> NormalizedEvents =>
            Set<NormalizedEventRecord>();
        internal DbSet<LastTradePriceEntity> LastTradePrices =>
            Set<LastTradePriceEntity>();
        internal DbSet<PriceChangeItemEntity> PriceChanges =>
            Set<PriceChangeItemEntity>();
        internal DbSet<BookSnapshotEntity> BookSnapshots =>
            Set<BookSnapshotEntity>();
        internal DbSet<BookLevelEntity> BookLevels =>
            Set<BookLevelEntity>();
        internal DbSet<TickSizeChangeEntity> TickSizeChanges =>
            Set<TickSizeChangeEntity>();
        internal DbSet<BestBidAskEntity> BestBidAsks =>
            Set<BestBidAskEntity>();
        internal DbSet<NewMarketEntity> NewMarkets =>
            Set<NewMarketEntity>();
        internal DbSet<NewMarketAssetEntity> NewMarketAssets =>
            Set<NewMarketAssetEntity>();
        internal DbSet<MarketResolutionEntity> MarketResolutions =>
            Set<MarketResolutionEntity>();
        internal DbSet<MarketResolutionAssetEntity> MarketResolutionAssets =>
            Set<MarketResolutionAssetEntity>();
        internal DbSet<ResolutionStateEntity> ResolutionStates =>
            Set<ResolutionStateEntity>();
        internal DbSet<ResolutionObservationEntity> ResolutionObservations =>
            Set<ResolutionObservationEntity>();
        internal DbSet<ResolutionObservationOutcomeEntity> ResolutionObservationOutcomes =>
            Set<ResolutionObservationOutcomeEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfigurationsFromAssembly(
                typeof(DataCollectionDbContext).Assembly);
        }
    }
}
