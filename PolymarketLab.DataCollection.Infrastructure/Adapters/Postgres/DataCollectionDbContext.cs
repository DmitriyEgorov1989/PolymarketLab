using Microsoft.EntityFrameworkCore;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres
{
    public sealed class DataCollectionDbContext(DbContextOptions<DataCollectionDbContext> options) : DbContext(options)
    {
        public DbSet<CollectorSession> CollectorSessions => Set<CollectorSession>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfigurationsFromAssembly(
                typeof(DataCollectionDbContext).Assembly);
        }
    }
}
