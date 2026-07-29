using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using System.Collections.Concurrent;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;

internal sealed class CollectorRuntime(
    ICollectorWorkerFactory workerFactory,
    ICollectorRuntimeFailureDispatcher failureDispatcher)
    : ICollectorRuntime
{
    private readonly ConcurrentDictionary<
        CollectorSessionId,
        Lazy<CollectorRuntimeEntry>> _entries = new();
    private readonly ConcurrentDictionary<Task, byte> _completionObservers = new();
    private int _shuttingDown;

    public async Task<UnitResult<Error>> StartAsync(
        CollectorRuntimeStartRequest request,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _shuttingDown) != 0)
        {
            return UnitResult.Failure(
                CollectorRuntimeErrors.RuntimeStopping(request.SessionId));
        }

        while (true)
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

            if (Volatile.Read(ref _shuttingDown) != 0)
            {
                await StopAsync(request.SessionId, CancellationToken.None);
                return UnitResult.Failure(
                    CollectorRuntimeErrors.RuntimeStopping(request.SessionId));
            }

            var completion = entry.ObserveCompletion();
            if (completion is not null)
            {
                TrackCompletionObserver(ObserveEntryCompletionAsync(
                    completion,
                    request.SessionId,
                    entryHolder));
            }

            var attempt = entry.Start(cancellationToken);
            if (attempt.RetryAfterCompletion)
            {
                await WaitForCompletionAsync(attempt.Task).WaitAsync(cancellationToken);
                RemoveEntry(request.SessionId, entryHolder);
                continue;
            }

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
    }

    internal async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _shuttingDown, 1);
        var sessionIds = _entries.Keys.ToArray();

        await Task.WhenAll(sessionIds.Select(sessionId =>
            StopAsync(sessionId, cancellationToken)));

        var completionObservers = _completionObservers.Keys.ToArray();
        if (completionObservers.Length > 0)
            await Task.WhenAll(completionObservers).WaitAsync(cancellationToken);
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

        var attempt = entry.Stop();
        if (attempt.IsOwner)
            _ = RemoveEntryWhenOperationCompletedAsync(
                attempt.Task,
                sessionId,
                entryHolder);

        return await attempt.Task.WaitAsync(cancellationToken);
    }

    private void RemoveEntry(
        CollectorSessionId sessionId,
        Lazy<CollectorRuntimeEntry> entryHolder)
    {
        _entries.TryRemove(new KeyValuePair<
            CollectorSessionId,
            Lazy<CollectorRuntimeEntry>>(sessionId, entryHolder));
    }

    private async Task ObserveEntryCompletionAsync(
        Task<CollectorWorkerCompletion> completionTask,
        CollectorSessionId sessionId,
        Lazy<CollectorRuntimeEntry> entryHolder)
    {
        CollectorWorkerCompletion completion;
        try
        {
            completion = await completionTask;
        }
        catch
        {
            RemoveEntry(sessionId, entryHolder);
            return;
        }

        RemoveEntry(sessionId, entryHolder);

        if (completion.Origin != CollectorWorkerCompletionOrigin.Autonomous
            || completion.Result.IsSuccess)
        {
            return;
        }

        await failureDispatcher.DispatchAsync(
            new CollectorRuntimeFailure(
                sessionId,
                completion.CompletedAt,
                completion.Result.Error),
            CancellationToken.None);
    }

    private async Task RemoveEntryWhenOperationCompletedAsync(
        Task task,
        CollectorSessionId sessionId,
        Lazy<CollectorRuntimeEntry> entryHolder)
    {
        await WaitForCompletionAsync(task);
        RemoveEntry(sessionId, entryHolder);
    }

    private void TrackCompletionObserver(Task observer)
    {
        _completionObservers.TryAdd(observer, 0);
        _ = observer.ContinueWith(
            completed => _completionObservers.TryRemove(completed, out _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static async Task WaitForCompletionAsync(Task task)
    {
        await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }
}
