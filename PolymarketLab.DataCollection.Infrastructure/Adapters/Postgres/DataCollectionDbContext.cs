using Microsoft.EntityFrameworkCore;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres
{
    public sealed class DataCollectionDbContext(DbContextOptions<DataCollectionDbContext> options) : DbContext(options)
    {
        public DbSet<CollectorSession> CollectorSessions => Set<CollectorSession>();
        internal DbSet<RawMarketMessageRecord> RawMarketMessages =>
            Set<RawMarketMessageRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfigurationsFromAssembly(
                typeof(DataCollectionDbContext).Assembly);
        }
    }
}
