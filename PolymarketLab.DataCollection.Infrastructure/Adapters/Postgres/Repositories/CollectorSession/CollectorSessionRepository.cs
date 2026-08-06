using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.CollectorSession;

internal sealed class CollectorSessionRepository(DataCollectionDbContext dbContext)
    : ICollectorSessionRepository
{
    private static readonly CollectorSessionStatus[] ActiveStatuses =
    [
        CollectorSessionStatus.Starting,
        CollectorSessionStatus.Running,
        CollectorSessionStatus.Stopping
    ];

    public Task<CollectorSessionAggregate?> GetByIdAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken)
    {
        return QuerySessions().SingleOrDefaultAsync(
            session => session.Id == sessionId,
            cancellationToken);
    }

    public Task<CollectorSessionAggregate?> GetActiveByMarketIdAsync(
        MarketId marketId,
        CancellationToken cancellationToken)
    {
        return QuerySessions().SingleOrDefaultAsync(
            session => session.MarketId == marketId
                && ActiveStatuses.Contains(session.Status),
            cancellationToken);
    }

    public Task<CollectorSessionAggregate?> GetCurrentByMarketIdAsync(
        MarketId marketId,
        CancellationToken cancellationToken)
    {
        return QuerySessions()
            .Where(session => session.MarketId == marketId)
            .OrderBy(session => ActiveStatuses.Contains(session.Status) ? 0 : 1)
            .ThenByDescending(session => session.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<CollectorSessionAggregate>> GetActiveAsync(
        CancellationToken cancellationToken)
    {
        return await QuerySessions()
            .Where(session => ActiveStatuses.Contains(session.Status))
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<CollectorSessionInsertStatus, Error>> TryAddAsync(
        CollectorSessionAggregate session,
        CancellationToken cancellationToken)
    {
        dbContext.CollectorSessions.Add(session);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return CollectorSessionInsertStatus.Inserted;
        }
        catch (DbUpdateException exception) when (IsActiveMarketConflict(exception))
        {
            dbContext.Entry(session).State = EntityState.Detached;
            return CollectorSessionInsertStatus.ActiveSessionConflict;
        }
    }

    public async Task<Result<CollectorSessionUpdateStatus, Error>> TryUpdateAsync(
        CollectorSessionAggregate session,
        CollectorSessionStatus expectedStatus,
        CancellationToken cancellationToken)
    {
        var entry = dbContext.Entry(session);
        if (entry.State == EntityState.Detached)
            dbContext.CollectorSessions.Attach(session);

        entry.State = EntityState.Modified;
        entry.Property(current => current.Status).OriginalValue = expectedStatus;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return CollectorSessionUpdateStatus.Updated;
        }
        catch (DbUpdateConcurrencyException)
        {
            entry.State = EntityState.Detached;
            return CollectorSessionUpdateStatus.ConcurrencyConflict;
        }
    }

    private IQueryable<CollectorSessionAggregate> QuerySessions()
    {
        return dbContext.CollectorSessions.AsNoTracking();
    }

    private static bool IsActiveMarketConflict(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException
            && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
            && CollectorSessionDatabaseConstraints.IsActiveMarketConstraint(
                postgresException.ConstraintName);
    }
}
