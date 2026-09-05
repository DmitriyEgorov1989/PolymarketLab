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
    private readonly ConcurrentDictionary<CollectorSessionId, AutonomousFailure> _autonomousFailures = new();
    private readonly ConcurrentDictionary<CollectorSessionId, byte> _fencedSessions = new();
    private int _shuttingDown;

    public void FenceSession(CollectorSessionId sessionId)
    {
        _fencedSessions.TryAdd(sessionId, 0);
    }

    public async Task<UnitResult<Error>> StartAsync(
        CollectorRuntimeStartRequest request,
        CancellationToken cancellationToken)
    {
        if (_fencedSessions.ContainsKey(request.SessionId))
        {
            return UnitResult.Failure(
                CollectorRuntimeErrors.SessionInvalidating(request.SessionId));
        }
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

            ObserveEntryCompletion(request.SessionId, entryHolder, entry);

            if (Volatile.Read(ref _shuttingDown) != 0)
            {
                await StopAsync(request.SessionId, CancellationToken.None);
                return UnitResult.Failure(
                    CollectorRuntimeErrors.RuntimeStopping(request.SessionId));
            }
            if (_fencedSessions.ContainsKey(request.SessionId))
            {
                await StopAsync(request.SessionId, CancellationToken.None);
                return UnitResult.Failure(
                    CollectorRuntimeErrors.SessionInvalidating(request.SessionId));
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
                else if (attempt.IsOwner)
                    _autonomousFailures.TryRemove(request.SessionId, out _);

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

    internal IReadOnlyCollection<CollectorRuntimeShutdownEntry> BeginShutdown()
    {
        Interlocked.Exchange(ref _shuttingDown, 1);
        return _entries
            .Select(entry => new CollectorRuntimeShutdownEntry(
                entry.Key,
                entry.Value))
            .ToArray();
    }

    internal async Task<IReadOnlyCollection<CollectorRuntimeShutdownResult>> ShutdownAsync(
        IReadOnlyCollection<CollectorRuntimeShutdownEntry> shutdownEntries,
        CancellationToken cancellationToken)
    {
        var results = await Task.WhenAll(shutdownEntries.Select(shutdownEntry =>
            StopForShutdownAsync(shutdownEntry, cancellationToken)));

        var completionObservers = _completionObservers.Keys.ToArray();
        if (completionObservers.Length > 0)
        {
            try
            {
                await Task.WhenAll(completionObservers).WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        return results;
    }

    internal Task<IReadOnlyCollection<CollectorRuntimeShutdownResult>> ShutdownAsync(
        CancellationToken cancellationToken)
    {
        return ShutdownAsync(BeginShutdown(), cancellationToken);
    }

    private async Task<CollectorRuntimeShutdownResult> StopForShutdownAsync(
        CollectorRuntimeShutdownEntry shutdownEntry,
        CancellationToken cancellationToken)
    {
        var autonomousFailure = GetAutonomousFailure(shutdownEntry);
        if (autonomousFailure is not null)
            return FailureResult(shutdownEntry.SessionId, autonomousFailure);

        try
        {
            var result = await StopAsync(
                shutdownEntry.SessionId,
                cancellationToken);
            autonomousFailure = GetAutonomousFailure(shutdownEntry);
            return autonomousFailure is null
                ? new CollectorRuntimeShutdownResult(
                    shutdownEntry.SessionId,
                    result)
                : FailureResult(
                    shutdownEntry.SessionId,
                    autonomousFailure);
        }
        catch (Exception exception)
        {
            return new CollectorRuntimeShutdownResult(
                shutdownEntry.SessionId,
                UnitResult.Failure(
                    CollectorRuntimeErrors.StopFailed(shutdownEntry.SessionId)),
                exception);
        }
    }

    private Error? GetAutonomousFailure(
        CollectorRuntimeShutdownEntry shutdownEntry)
    {
        return _autonomousFailures.TryGetValue(
                   shutdownEntry.SessionId,
                   out var failure)
               && ReferenceEquals(
                   failure.EntryHolder,
                   shutdownEntry.EntryHolder)
            ? failure.Error
            : null;
    }

    private static CollectorRuntimeShutdownResult FailureResult(
        CollectorSessionId sessionId,
        Error error)
    {
        return new CollectorRuntimeShutdownResult(
            sessionId,
            UnitResult.Failure(error));
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

        ObserveEntryCompletion(sessionId, entryHolder, entry);

        if (_fencedSessions.ContainsKey(sessionId) && entry.IsCompleted)
            return UnitResult.Success<Error>();

        var attempt = entry.Stop();
        if (attempt.IsOwner)
            _ = RemoveEntryWhenOperationCompletedAsync(
                attempt.Task,
                sessionId,
                entryHolder);

        var result = await attempt.Task.WaitAsync(cancellationToken);
        if (result.IsSuccess && _fencedSessions.ContainsKey(sessionId) && !entry.IsCompleted)
            return UnitResult.Failure(CollectorRuntimeErrors.StopFailed(sessionId));

        return result;
    }

    private void RemoveEntry(
        CollectorSessionId sessionId,
        Lazy<CollectorRuntimeEntry> entryHolder)
    {
        // A fenced producer must finish before an absent entry can authorize dataset cleanup.
        if (_fencedSessions.ContainsKey(sessionId)
            && entryHolder.IsValueCreated
            && !entryHolder.Value.IsCompleted)
            return;

        _entries.TryRemove(new KeyValuePair<
            CollectorSessionId,
            Lazy<CollectorRuntimeEntry>>(sessionId, entryHolder));
    }

    private void ObserveEntryCompletion(
        CollectorSessionId sessionId,
        Lazy<CollectorRuntimeEntry> entryHolder,
        CollectorRuntimeEntry entry)
    {
        var completion = entry.ObserveCompletion();
        if (completion is not null)
        {
            TrackCompletionObserver(ObserveEntryCompletionAsync(
                completion,
                sessionId,
                entryHolder));
        }
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

        if (completion.Origin != CollectorWorkerCompletionOrigin.Autonomous
            || completion.Result.IsSuccess)
        {
            RemoveEntry(sessionId, entryHolder);
            return;
        }

        var autonomousFailure = new AutonomousFailure(
            entryHolder,
            completion.Result.Error);
        _autonomousFailures[sessionId] = autonomousFailure;
        RemoveEntry(sessionId, entryHolder);

        var persisted = await failureDispatcher.DispatchAsync(
            new CollectorRuntimeFailure(
                sessionId,
                completion.CompletedAt,
                completion.Result.Error),
            CancellationToken.None);
        if (persisted)
        {
            _autonomousFailures.TryRemove(
                new KeyValuePair<CollectorSessionId, AutonomousFailure>(
                    sessionId,
                    autonomousFailure));
        }
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

    private sealed record AutonomousFailure(
        Lazy<CollectorRuntimeEntry> EntryHolder,
        Error Error);
}
