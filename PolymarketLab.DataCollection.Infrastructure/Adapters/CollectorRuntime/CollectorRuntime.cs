using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using System.Collections.Concurrent;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;

internal sealed class CollectorRuntime(ICollectorWorkerFactory workerFactory)
    : ICollectorRuntime
{
    private readonly ConcurrentDictionary<
        CollectorSessionId,
        Lazy<CollectorRuntimeEntry>> _entries = new();

    public async Task<UnitResult<Error>> StartAsync(
        CollectorRuntimeStartRequest request,
        CancellationToken cancellationToken)
    {
        var entryHolder = _entries.GetOrAdd(
            request.SessionId,
            _ => new Lazy<CollectorRuntimeEntry>(
                () => new CollectorRuntimeEntry(workerFactory.Create(request)),
                LazyThreadSafetyMode.ExecutionAndPublication));

        CollectorRuntimeEntry entry;
        try
        {
            entry = entryHolder.Value;
        }
        catch
        {
            RemoveEntry(request.SessionId, entryHolder);
            throw;
        }

        var attempt = entry.Start(cancellationToken);

        try
        {
            var result = attempt.IsOwner
                ? await attempt.Task
                : await attempt.Task.WaitAsync(cancellationToken);

            if (attempt.IsOwner && result.IsFailure)
                RemoveEntry(request.SessionId, entryHolder);

            return result;
        }
        catch
        {
            if (attempt.IsOwner)
                RemoveEntry(request.SessionId, entryHolder);

            throw;
        }
    }

    public async Task<UnitResult<Error>> StopAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken)
    {
        if (!_entries.TryGetValue(sessionId, out var entryHolder))
            return UnitResult.Success<Error>();

        CollectorRuntimeEntry entry;
        try
        {
            entry = entryHolder.Value;
        }
        catch
        {
            RemoveEntry(sessionId, entryHolder);
            throw;
        }

        var attempt = entry.Stop(cancellationToken);
        try
        {
            var result = attempt.IsOwner
                ? await attempt.Task
                : await attempt.Task.WaitAsync(cancellationToken);

            if (attempt.IsOwner)
                RemoveEntry(sessionId, entryHolder);

            return result;
        }
        catch
        {
            if (attempt.IsOwner)
                RemoveEntry(sessionId, entryHolder);

            throw;
        }
    }

    private void RemoveEntry(
        CollectorSessionId sessionId,
        Lazy<CollectorRuntimeEntry> entryHolder)
    {
        _entries.TryRemove(new KeyValuePair<
            CollectorSessionId,
            Lazy<CollectorRuntimeEntry>>(sessionId, entryHolder));
    }
}
