using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Tests.TestSupport;

internal sealed class StubCollectorDatasetCleanup(Error? error = null)
    : ICollectorDatasetCleanup
{
    public List<CollectorSessionId> Calls { get; } = [];

    public Task<Result<CollectorDatasetCleanupAudit, Error>> CleanupAsync(
        CollectorSessionAggregate session,
        CancellationToken cancellationToken)
    {
        Calls.Add(session.Id);
        return error is null
            ? Complete(session)
            : Task.FromResult(Result.Failure<CollectorDatasetCleanupAudit, Error>(error));
    }

    private static Task<Result<CollectorDatasetCleanupAudit, Error>> Complete(
        CollectorSessionAggregate session)
    {
        var completedAt = session.Status == CollectorSessionStatus.Invalidating
            ? session.InvalidatingAt!.Value.AddSeconds(1)
            : session.RetainUntil!.Value;
        if (session.Status == CollectorSessionStatus.Invalidating)
            session.CompleteInvalidation(completedAt);
        else
            session.CompleteRetentionExpiry(completedAt);
        return Task.FromResult(Result.Success<CollectorDatasetCleanupAudit, Error>(
            new CollectorDatasetCleanupAudit(session.Id, completedAt, 0, 0, 0)));
    }
}
