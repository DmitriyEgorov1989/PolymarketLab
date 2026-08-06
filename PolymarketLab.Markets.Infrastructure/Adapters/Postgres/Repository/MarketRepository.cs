using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PolymarketLab.Markets.Core.Domain.Models.Market.Entity;
using PolymarketLab.Markets.Core.Domain.Models.Market.MarketAggregate;
using PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects;
using PolymarketLab.Markets.Core.Ports;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.Markets.Infrastructure.Adapters.Postgres.Repository;

internal sealed class MarketRepository(MarketsDbContext dbContext) : IMarketRepository
{
    public async Task<IReadOnlyCollection<Market>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        return await QueryMarkets()
            .OrderBy(market => market.Slug.Value)
            .ToArrayAsync(cancellationToken);
    }

    public Task<Market?> GetByIdAsync(
        MarketId marketId,
        CancellationToken cancellationToken)
    {
        return QueryMarkets()
            .SingleOrDefaultAsync(
                market => market.Id == marketId,
                cancellationToken);
    }

    public Task<Market?> GetBySlugAsync(
        MarketSlug slug,
        CancellationToken cancellationToken)
    {
        return QueryMarkets()
            .SingleOrDefaultAsync(market =>
            market.Slug == slug, cancellationToken);
    }

    public Task<Market?> GetByExternalIdAsync(
        ExternalMarketId externalMarketId,
        CancellationToken cancellationToken)
    {
        return QueryMarkets()
            .SingleOrDefaultAsync(
                market => market.ExternalId == externalMarketId,
                cancellationToken);
    }

    public Task<Market?> GetByConditionIdAsync(
        ConditionId conditionId,
        CancellationToken cancellationToken)
    {
        return QueryMarkets()
            .SingleOrDefaultAsync(
                market => market.ConditionId == conditionId,
                cancellationToken);
    }

    public async Task<Result<MarketInsertStatus, Error>> TryAddAsync(
        Market market,
        CancellationToken cancellationToken)
    {
        dbContext.Markets.Add(market);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return MarketInsertStatus.Inserted;
        }
        catch (DbUpdateException exception) when
        (IsIdentityConflict(exception))
        {
            Detach(market);
            return MarketInsertStatus.UniqueConflict;
        }
    }

    private IQueryable<Market> QueryMarkets()
    {
        return dbContext.Markets
            .AsNoTracking()
            .Include(market => market.Tokens);
    }

    private static bool IsIdentityConflict(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException
            && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
            && MarketDatabaseConstraints.IsIdentityConstraint(postgresException.ConstraintName);
    }

    private void Detach(Market market)
    {
        foreach (MarketToken token in market.Tokens)
            dbContext.Entry(token).State = EntityState.Detached;

        dbContext.Entry(market).State = EntityState.Detached;
    }
}
