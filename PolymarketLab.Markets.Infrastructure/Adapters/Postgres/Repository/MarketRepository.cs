using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
            .OrderBy(market => market.MarketSlug)
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

    public Task<Market?> GetByEventSlugAsync(
        EventSlug eventSlug,
        CancellationToken cancellationToken)
    {
        return QueryMarkets().SingleOrDefaultAsync(
            market => market.EventSlug == eventSlug,
            cancellationToken);
    }

    public Task<Market?> GetByExternalEventIdAsync(
        ExternalEventId externalEventId,
        CancellationToken cancellationToken)
    {
        return QueryMarkets().SingleOrDefaultAsync(
            market => market.ExternalEventId == externalEventId,
            cancellationToken);
    }

    public Task<Market?> GetBySlugAsync(
        MarketSlug slug,
        CancellationToken cancellationToken)
    {
        return QueryMarkets()
            .SingleOrDefaultAsync(market =>
            market.MarketSlug == slug, cancellationToken);
    }

    public Task<Market?> GetByExternalIdAsync(
        ExternalMarketId externalMarketId,
        CancellationToken cancellationToken)
    {
        return QueryMarkets()
            .SingleOrDefaultAsync(
                market => market.ExternalMarketId == externalMarketId,
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
            Detach();
            return MarketInsertStatus.UniqueConflict;
        }
    }

    public async Task<IReadOnlyCollection<Market>> GetByAnyTokenIdsAsync(
        IReadOnlyCollection<TokenId> tokenIds,
        CancellationToken cancellationToken)
    {
        return await QueryMarkets()
            .Where(market => market.Tokens.Any(token => tokenIds.Contains(token.ExternalTokenId)))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<UnitResult<Error>> UpdateScheduleAsync(
        Market market,
        CancellationToken cancellationToken)
    {
        await dbContext.Markets
            .Where(stored => stored.Id == market.Id
                && stored.ScheduleRefreshedAt < market.ScheduleRefreshedAt)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(stored => stored.ExternalCreatedAt, market.ExternalCreatedAt)
                    .SetProperty(stored => stored.OrdersOpenedAt, market.OrdersOpenedAt)
                    .SetProperty(stored => stored.GammaStartDate, market.GammaStartDate)
                    .SetProperty(stored => stored.EventStartsAt, market.EventStartsAt)
                    .SetProperty(stored => stored.EventEndsAt, market.EventEndsAt)
                    .SetProperty(stored => stored.ExternalClosedAt, market.ExternalClosedAt)
                    .SetProperty(stored => stored.ScheduleRefreshedAt, market.ScheduleRefreshedAt),
                cancellationToken);

        return UnitResult.Success<Error>();
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

    private void Detach()
    {
        dbContext.ChangeTracker.Clear();
    }
}
