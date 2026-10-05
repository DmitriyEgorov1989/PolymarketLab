using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.CollectorSession;

internal sealed class CollectorSessionRepository : ICollectorSessionRepository
{
    private const long DiagnosticQuotaLockKey = 0x504D4C444941474E;

    private readonly DataCollectionDbContext _dbContext;
    private readonly int _maximumRetainedSessions;

    private static readonly CollectorSessionStatus[] ExclusiveStatuses =
    [
        CollectorSessionStatus.Scheduled,
        CollectorSessionStatus.Starting,
        CollectorSessionStatus.Running,
        CollectorSessionStatus.Stopping,
        CollectorSessionStatus.Invalidating
    ];

    public CollectorSessionRepository(DataCollectionDbContext dbContext)
        : this(dbContext, Options.Create(new FailedDatasetRetentionOptions()))
    {
    }

    public CollectorSessionRepository(
        DataCollectionDbContext dbContext,
        IOptions<FailedDatasetRetentionOptions> options)
    {
        _dbContext = dbContext;
        _maximumRetainedSessions = options.Value.MaximumRetainedSessions;
    }

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

    public async Task<IReadOnlyCollection<CollectorSessionAggregate>> GetCurrentAsync(
        CancellationToken cancellationToken)
    {
        var sessions = await QuerySessions()
            .OrderBy(session => session.MarketId)
            .ThenBy(session => ExclusiveStatuses.Contains(session.Status) ? 0 : 1)
            .ThenByDescending(session => session.CreatedAt)
            .ToListAsync(cancellationToken);

        return sessions
            .GroupBy(session => session.MarketId)
            .Select(group => group.First())
            .ToArray();
    }

    public Task<CollectorSessionAggregate?> GetSuccessfulByMarketIdAsync(
        MarketId marketId,
        CancellationToken cancellationToken)
    {
        return QuerySessions()
            .Where(session => session.MarketId == marketId
                && session.Status == CollectorSessionStatus.Stopped
                && session.StopReason == CollectorStopReason.MarketClosed)
            .OrderByDescending(session => session.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<CollectorSessionAggregate>> GetActiveAsync(
        CancellationToken cancellationToken)
    {
        return await QuerySessions()
            .Where(session => ExclusiveStatuses.Contains(session.Status))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<CollectorSessionAggregate>> GetExpiredRetainedAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        return await QuerySessions()
            .Where(session => session.Status == CollectorSessionStatus.Failed
                && session.DatasetDisposition == CollectorDatasetDisposition.Retained
                && session.RetainUntil <= now)
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<CollectorSessionInsertStatus, Error>> TryAddAsync(
        CollectorSessionAggregate session,
        CancellationToken cancellationToken)
    {
        if (session.FailurePolicy == CollectorFailurePolicy.RetainOnFailure)
            return await TryAddDiagnosticAsync(session, cancellationToken);

        return await InsertAsync(session, cancellationToken);
    }

    private async Task<Result<CollectorSessionInsertStatus, Error>> TryAddDiagnosticAsync(
        CollectorSessionAggregate session,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.IsNpgsql())
        {
            await using var transaction = await _dbContext.Database
                .BeginTransactionAsync(cancellationToken);
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({DiagnosticQuotaLockKey})",
                cancellationToken);

            var result = await InsertWithinDiagnosticQuotaAsync(
                session,
                cancellationToken);
            if (result.IsSuccess
                && result.Value == CollectorSessionInsertStatus.Inserted)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return result;
        }

        return await InsertWithinDiagnosticQuotaAsync(session, cancellationToken);
    }

    private async Task<Result<CollectorSessionInsertStatus, Error>> InsertWithinDiagnosticQuotaAsync(
        CollectorSessionAggregate session,
        CancellationToken cancellationToken)
    {
        var reservationCount = await _dbContext.CollectorSessions.CountAsync(
            candidate => candidate.FailurePolicy == CollectorFailurePolicy.RetainOnFailure
                && (ExclusiveStatuses.Contains(candidate.Status)
                    || (candidate.Status == CollectorSessionStatus.Failed
                        && candidate.DatasetDisposition ==
                            CollectorDatasetDisposition.Retained)),
            cancellationToken);
        if (reservationCount >= _maximumRetainedSessions)
            return CollectorSessionInsertStatus.DiagnosticQuotaExceeded;

        return await InsertAsync(session, cancellationToken);
    }

    private async Task<Result<CollectorSessionInsertStatus, Error>> InsertAsync(
        CollectorSessionAggregate session,
        CancellationToken cancellationToken)
    {
        _dbContext.CollectorSessions.Add(session);
        var progress = new CollectorSessionProgressRecord(session.Id);
        _dbContext.CollectorSessionProgress.Add(progress);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return CollectorSessionInsertStatus.Inserted;
        }
        catch (DbUpdateException exception) when (IsActiveMarketConflict(exception))
        {
            _dbContext.Entry(session).State = EntityState.Detached;
            _dbContext.Entry(progress).State = EntityState.Detached;
            foreach (var token in session.Tokens)
                _dbContext.Entry(token).State = EntityState.Detached;
            return CollectorSessionInsertStatus.ActiveMarketConflict;
        }
    }

    public async Task<Result<CollectorSessionUpdateStatus, Error>> TryUpdateAsync(
        CollectorSessionAggregate session,
        CollectorSessionStatus expectedStatus,
        CancellationToken cancellationToken)
    {
        var entry = _dbContext.Entry(session);
        if (entry.State == EntityState.Detached)
            _dbContext.CollectorSessions.Attach(session);

        entry.State = EntityState.Modified;
        entry.Property(current => current.Status).OriginalValue = expectedStatus;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            Detach(session);
            return CollectorSessionUpdateStatus.Updated;
        }
        catch (DbUpdateConcurrencyException)
        {
            Detach(session);
            return CollectorSessionUpdateStatus.ConcurrencyConflict;
        }
    }

    private void Detach(CollectorSessionAggregate session)
    {
        _dbContext.Entry(session).State = EntityState.Detached;
        foreach (var token in session.Tokens)
            _dbContext.Entry(token).State = EntityState.Detached;
    }

    private IQueryable<CollectorSessionAggregate> QuerySessions()
    {
        return _dbContext.CollectorSessions
            .Include(session => session.Tokens.OrderBy(token => token.OutcomeIndex))
            .AsNoTracking();
    }

    private static bool IsActiveMarketConflict(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException
            && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
             && CollectorSessionDatabaseConstraints.IsActiveMarketConstraint(
                 postgresException.ConstraintName);
    }
}
