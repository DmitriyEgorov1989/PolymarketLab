using Microsoft.EntityFrameworkCore;
using PolymarketLab.Markets.Core.Domain.Models.Market.MarketAggregate;

namespace PolymarketLab.Markets.Infrastructure.Adapters.Postgres;

public sealed class MarketsDbContext(DbContextOptions<MarketsDbContext> options)
    : DbContext(options)
{
    public DbSet<Market> Markets => Set<Market>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MarketsDbContext).Assembly);
    }
}