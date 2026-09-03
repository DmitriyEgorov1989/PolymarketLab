using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.CollectorSession;

internal sealed class CollectorDatasetCleanup(
    DataCollectionDbContext dbContext,
    TimeProvider timeProvider) : ICollectorDatasetCleanup
{
    public async Task<Result<CollectorDatasetCleanupAudit, Error>> CleanupAsync(
        CollectorSessionAggregate session,
        CancellationToken cancellationToken)
    {
        var sessionId = session.Id;
        CollectorDatasetCleanupAudit? committedAudit = null;
        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);
        try
        {
            var persistedStatus = await LockSessionAsync(sessionId, cancellationToken);
            if (persistedStatus is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return CollectorDatasetCleanupErrors.SessionNotFound(sessionId);
            }

            if (persistedStatus == CollectorSessionStatus.Failed)
            {
                var existingAudit = await dbContext.CollectorDatasetCleanupAudits
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        audit => audit.SessionId == sessionId,
                        cancellationToken);
                await transaction.RollbackAsync(cancellationToken);
                if (existingAudit is not null
                    && session.Status == CollectorSessionStatus.Invalidating)
                {
                    session.CompleteInvalidation(existingAudit.CompletedAt);
                }

                return existingAudit is null
                    ? CollectorDatasetCleanupErrors.InvalidStatus(
                        sessionId,
                        persistedStatus.Value)
                    : existingAudit.ToAudit();
            }

            if (persistedStatus != CollectorSessionStatus.Invalidating)
            {
                await transaction.RollbackAsync(cancellationToken);
                return CollectorDatasetCleanupErrors.InvalidStatus(
                    sessionId,
                    persistedStatus.Value);
            }

            if (session.Status != CollectorSessionStatus.Invalidating)
            {
                await transaction.RollbackAsync(cancellationToken);
                return CollectorDatasetCleanupErrors.StateTransitionConflict(sessionId);
            }

            var deletedEvents = await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM data_collection.normalized_events AS normalized
                WHERE EXISTS (
                    SELECT 1
                    FROM data_collection.raw_market_messages AS raw
                    WHERE raw.id = normalized.raw_message_id
                      AND raw.session_id = {sessionId.Value})
                """,
                cancellationToken);
            var deletedNormalizations = await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM data_collection.raw_message_normalizations AS normalization
                WHERE EXISTS (
                    SELECT 1
                    FROM data_collection.raw_market_messages AS raw
                    WHERE raw.id = normalization.raw_message_id
                      AND raw.session_id = {sessionId.Value})
                """,
                cancellationToken);
            var deletedRawMessages = await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM data_collection.raw_market_messages
                WHERE session_id = {sessionId.Value}
                """,
                cancellationToken);

            var now = timeProvider.GetUtcNow();
            var completedAt = session.InvalidatingAt is not null
                && now < session.InvalidatingAt.Value
                    ? session.InvalidatingAt.Value
                    : now;
            var audit = new CollectorDatasetCleanupAudit(
                sessionId,
                completedAt,
                deletedRawMessages,
                deletedNormalizations,
                deletedEvents);
            dbContext.CollectorDatasetCleanupAudits.Add(
                new CollectorDatasetCleanupAuditRecord(audit));
            await dbContext.SaveChangesAsync(cancellationToken);

            var transitioned = await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE data_collection.collector_sessions
                SET status = {(int)CollectorSessionStatus.Failed},
                    phase = NULL,
                    stopped_at = {completedAt}
                WHERE id = {sessionId.Value}
                  AND status = {(int)CollectorSessionStatus.Invalidating}
                """,
                cancellationToken);
            if (transitioned != 1)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                dbContext.ChangeTracker.Clear();
                return CollectorDatasetCleanupErrors.StateTransitionConflict(sessionId);
            }

            await transaction.CommitAsync(cancellationToken);
            committedAudit = audit;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            throw;
        }

        var completion = session.CompleteInvalidation(committedAudit.CompletedAt);
        if (completion.IsFailure)
        {
            throw new InvalidOperationException(
                $"Collector session '{sessionId.Value}' could not reflect committed dataset cleanup: {completion.Error.Code}.");
        }

        return committedAudit;
    }

    private async Task<CollectorSessionStatus?> LockSessionAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken)
    {
        var statuses = await dbContext.Database.SqlQueryRaw<int>(
                """
                SELECT status AS "Value"
                FROM data_collection.collector_sessions
                WHERE id = {0}
                FOR UPDATE
                """,
                sessionId.Value)
            .ToArrayAsync(cancellationToken);
        return statuses.Length == 0 ? null : (CollectorSessionStatus)statuses[0];
    }
}
