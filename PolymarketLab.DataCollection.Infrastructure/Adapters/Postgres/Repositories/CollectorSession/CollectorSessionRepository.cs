using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.CollectorSession;

internal sealed class CollectorSessionRepository(DataCollectionDbContext dbContext)
    : ICollectorSessionRepository
{
    private static readonly CollectorSessionStatus[] ExclusiveStatuses =
    [
        CollectorSessionStatus.Scheduled,
        CollectorSessionStatus.Starting,
        CollectorSessionStatus.Running,
        CollectorSessionStatus.Stopping,
        CollectorSessionStatus.Invalidating
    ];

    public Task<CollectorSessionAggregate?> GetByIdAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken)
    {
        return QuerySessions().SingleOrDefaultAsync(
            session => session.Id == sessionId,
            cancellationToken);
    }

    public Task<CollectorSessionAggregate?> GetExclusiveAsync(
        CancellationToken cancellationToken)
    {
        return QuerySessions().SingleOrDefaultAsync(
            session => ExclusiveStatuses.Contains(session.Status),
            cancellationToken);
    }

    public Task<CollectorSessionAggregate?> GetActiveByMarketIdAsync(
        MarketId marketId,
        CancellationToken cancellationToken)
    {
        return QuerySessions().SingleOrDefaultAsync(
            session => session.MarketId == marketId
                && ExclusiveStatuses.Contains(session.Status),
            cancellationToken);
    }

    public Task<CollectorSessionAggregate?> GetCurrentByMarketIdAsync(
        MarketId marketId,
        CancellationToken cancellationToken)
    {
        return QuerySessions()
            .Where(session => session.MarketId == marketId)
            .OrderBy(session => ExclusiveStatuses.Contains(session.Status) ? 0 : 1)
            .ThenByDescending(session => session.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<CollectorSessionAggregate>> GetActiveAsync(
        CancellationToken cancellationToken)
    {
        return await QuerySessions()
            .Where(session => ExclusiveStatuses.Contains(session.Status))
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<CollectorSessionInsertStatus, Error>> TryAddAsync(
        CollectorSessionAggregate session,
        CancellationToken cancellationToken)
    {
        dbContext.CollectorSessions.Add(session);
        var progress = new CollectorSessionProgressRecord(session.Id);
        dbContext.CollectorSessionProgress.Add(progress);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return CollectorSessionInsertStatus.Inserted;
        }
        catch (DbUpdateException exception) when (IsExclusiveSlotConflict(exception))
        {
            dbContext.Entry(session).State = EntityState.Detached;
            dbContext.Entry(progress).State = EntityState.Detached;
            foreach (var token in session.Tokens)
                dbContext.Entry(token).State = EntityState.Detached;
            return CollectorSessionInsertStatus.ExclusiveSessionConflict;
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
        return dbContext.CollectorSessions
            .Include(session => session.Tokens.OrderBy(token => token.OutcomeIndex))
            .AsNoTracking();
    }

    private static bool IsExclusiveSlotConflict(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException
            && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
            && CollectorSessionDatabaseConstraints.IsExclusiveSlotConstraint(
                postgresException.ConstraintName);
    }
}
