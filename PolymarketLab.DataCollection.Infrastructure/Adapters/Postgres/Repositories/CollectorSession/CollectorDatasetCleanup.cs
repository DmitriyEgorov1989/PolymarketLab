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
            var persisted = await LockSessionAsync(sessionId, cancellationToken);
            if (persisted is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return CollectorDatasetCleanupErrors.SessionNotFound(sessionId);
            }

            var persistedStatus = (CollectorSessionStatus)persisted.Status;
            var persistedDisposition = (CollectorDatasetDisposition?)persisted.DatasetDisposition;
            if (persistedStatus == CollectorSessionStatus.Failed
                && persistedDisposition == CollectorDatasetDisposition.Deleted)
            {
                var existingAudit = await dbContext.CollectorDatasetCleanupAudits
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        audit => audit.SessionId == sessionId,
                        cancellationToken);
                await transaction.RollbackAsync(cancellationToken);
                if (existingAudit is null)
                {
                    return CollectorDatasetCleanupErrors.InvalidStatus(
                        sessionId,
                        persistedStatus);
                }

                ReflectCommittedCleanup(session, persisted.StoppedAt, existingAudit.ToAudit());
                return existingAudit.ToAudit();
            }

            var now = timeProvider.GetUtcNow();
            var canCleanInvalidating = persistedStatus == CollectorSessionStatus.Invalidating
                && (CollectorFailurePolicy)persisted.FailurePolicy
                    == CollectorFailurePolicy.DeleteOnFailure;
            var canCleanExpiredRetention = persistedStatus == CollectorSessionStatus.Failed
                && persistedDisposition == CollectorDatasetDisposition.Retained
                && persisted.RetainUntil is not null
                && persisted.RetainUntil <= now;
            if (!canCleanInvalidating && !canCleanExpiredRetention)
            {
                await transaction.RollbackAsync(cancellationToken);
                return CollectorDatasetCleanupErrors.InvalidStatus(sessionId, persistedStatus);
            }

            if ((canCleanInvalidating
                    && session.Status != CollectorSessionStatus.Invalidating)
                || (canCleanExpiredRetention
                    && (session.Status != CollectorSessionStatus.Failed
                        || session.DatasetDisposition != CollectorDatasetDisposition.Retained)))
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

            var completedAt = canCleanInvalidating
                && persisted.InvalidatingAt is not null
                && now < persisted.InvalidatingAt.Value
                    ? persisted.InvalidatingAt.Value
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

            var transitioned = canCleanInvalidating
                ? await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE data_collection.collector_sessions
                    SET status = {(int)CollectorSessionStatus.Failed},
                        phase = NULL,
                        stopped_at = {completedAt},
                        dataset_disposition = {(int)CollectorDatasetDisposition.Deleted}
                    WHERE id = {sessionId.Value}
                      AND status = {(int)CollectorSessionStatus.Invalidating}
                      AND failure_policy = {(int)CollectorFailurePolicy.DeleteOnFailure}
                    """,
                    cancellationToken)
                : await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE data_collection.collector_sessions
                    SET dataset_disposition = {(int)CollectorDatasetDisposition.Deleted}
                    WHERE id = {sessionId.Value}
                      AND status = {(int)CollectorSessionStatus.Failed}
                      AND dataset_disposition = {(int)CollectorDatasetDisposition.Retained}
                      AND retain_until <= {now}
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

        ReflectCommittedCleanup(session, session.StoppedAt, committedAudit);

        return committedAudit;
    }

    private async Task<LockedSessionState?> LockSessionAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken)
    {
        var sessions = await dbContext.Database.SqlQueryRaw<LockedSessionState>(
                """
                SELECT status AS "Status",
                       failure_policy AS "FailurePolicy",
                       dataset_disposition AS "DatasetDisposition",
                       invalidating_at AS "InvalidatingAt",
                       stopped_at AS "StoppedAt",
                       retain_until AS "RetainUntil"
                FROM data_collection.collector_sessions
                WHERE id = {0}
                FOR UPDATE
                """,
                sessionId.Value)
            .ToArrayAsync(cancellationToken);
        return sessions.Length == 0 ? null : sessions[0];
    }

    private static void ReflectCommittedCleanup(
        CollectorSessionAggregate session,
        DateTimeOffset? stoppedAt,
        CollectorDatasetCleanupAudit audit)
    {
        if (session.Status == CollectorSessionStatus.Invalidating)
        {
            var completion = session.CompleteInvalidation(stoppedAt ?? audit.CompletedAt);
            ThrowIfReflectionFailed(session.Id, completion);
        }

        if (session.Status == CollectorSessionStatus.Failed
            && session.DatasetDisposition == CollectorDatasetDisposition.Retained)
        {
            var completion = session.CompleteRetentionExpiry(audit.CompletedAt);
            ThrowIfReflectionFailed(session.Id, completion);
        }

        if (session.Status != CollectorSessionStatus.Failed
            || session.DatasetDisposition != CollectorDatasetDisposition.Deleted)
        {
            throw new InvalidOperationException(
                $"Collector session '{session.Id.Value}' could not reflect committed dataset cleanup.");
        }
    }

    private static void ThrowIfReflectionFailed(
        CollectorSessionId sessionId,
        UnitResult<Error> completion)
    {
        if (completion.IsFailure)
        {
            throw new InvalidOperationException(
                $"Collector session '{sessionId.Value}' could not reflect committed dataset cleanup: {completion.Error.Code}.");
        }
    }

    private sealed class LockedSessionState
    {
        public int Status { get; init; }
        public int FailurePolicy { get; init; }
        public int? DatasetDisposition { get; init; }
        public DateTimeOffset? InvalidatingAt { get; init; }
        public DateTimeOffset? StoppedAt { get; init; }
        public DateTimeOffset? RetainUntil { get; init; }
    }
}
