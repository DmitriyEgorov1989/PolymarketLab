using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;

internal sealed class RawMarketMessageTelemetry : IDisposable
{
    private readonly Meter _meter = new(
        "PolymarketLab.DataCollection.RawMessages",
        "1.0.0");
    private readonly Counter<long> _receivedCompleteCounter;
    private readonly Counter<long> _enqueuedCounter;
    private readonly Counter<long> _persistedCounter;
    private readonly Counter<long> _reconnectCounter;
    private readonly ConcurrentDictionary<CollectorSessionId, CounterState> _states = new();

    public RawMarketMessageTelemetry()
    {
        _receivedCompleteCounter = _meter.CreateCounter<long>(
            "raw_messages.received_complete");
        _enqueuedCounter = _meter.CreateCounter<long>("raw_messages.enqueued");
        _persistedCounter = _meter.CreateCounter<long>("raw_messages.persisted");
        _reconnectCounter = _meter.CreateCounter<long>("collector.reconnects");
    }

    public RawMarketMessageCounters RecordReceivedComplete(
        CollectorSessionId sessionId,
        DateTimeOffset receivedAt)
    {
        _receivedCompleteCounter.Add(1, CreateTags(sessionId));
        return _states.GetOrAdd(sessionId, _ => new CounterState())
            .IncrementReceivedComplete(receivedAt);
    }

    public RawMarketMessageCounters RecordEnqueued(CollectorSessionId sessionId)
    {
        _enqueuedCounter.Add(1, CreateTags(sessionId));
        return _states.GetOrAdd(sessionId, _ => new CounterState())
            .IncrementEnqueued();
    }

    public RawMarketMessageCounters RecordPersisted(
        CollectorSessionId sessionId,
        long count)
    {
        if (count <= 0)
            return GetSnapshot(sessionId);

        _persistedCounter.Add(count, CreateTags(sessionId));
        return _states.GetOrAdd(sessionId, _ => new CounterState())
            .IncrementPersisted(count);
    }

    public RawMarketMessageCounters RecordReconnect(CollectorSessionId sessionId)
    {
        _reconnectCounter.Add(1, CreateTags(sessionId));
        return _states.GetOrAdd(sessionId, _ => new CounterState())
            .IncrementReconnect();
    }

    public RawMarketMessageCounters GetSnapshot(CollectorSessionId sessionId)
    {
        return _states.TryGetValue(sessionId, out var state)
            ? state.GetSnapshot()
            : new RawMarketMessageCounters(0, 0, 0);
    }

    public CollectorSessionProgressCheckpoint GetCheckpoint(CollectorSessionId sessionId)
    {
        var snapshot = GetSnapshot(sessionId);
        return new CollectorSessionProgressCheckpoint(
            sessionId,
            snapshot.ReceivedComplete,
            snapshot.LastMessageAt,
            snapshot.ReconnectCount);
    }

    public Task WaitUntilPersistedAsync(
        CollectorSessionId sessionId,
        long target,
        CancellationToken cancellationToken)
    {
        return _states.GetOrAdd(sessionId, _ => new CounterState())
            .WaitUntilPersistedAsync(target, cancellationToken);
    }

    public void Dispose()
    {
        _meter.Dispose();
    }

    private static TagList CreateTags(CollectorSessionId sessionId)
    {
        return new TagList
        {
            { "session_id", sessionId.Value }
        };
    }

    private sealed class CounterState
    {
        private long _receivedComplete;
        private long _enqueued;
        private long _persisted;
        private long _lastMessageUtcTicks;
        private long _reconnectCount;
        private readonly object _persistedSignalLock = new();
        private TaskCompletionSource _persistedChanged = CreateSignal();

        public RawMarketMessageCounters IncrementReceivedComplete(DateTimeOffset receivedAt)
        {
            Interlocked.Increment(ref _receivedComplete);
            UpdateMaximum(ref _lastMessageUtcTicks, receivedAt.UtcTicks);
            return GetSnapshot();
        }

        public RawMarketMessageCounters IncrementEnqueued()
        {
            Interlocked.Increment(ref _enqueued);
            return GetSnapshot();
        }

        public RawMarketMessageCounters IncrementPersisted(long count)
        {
            Interlocked.Add(ref _persisted, count);
            TaskCompletionSource signal;
            lock (_persistedSignalLock)
            {
                signal = _persistedChanged;
                _persistedChanged = CreateSignal();
            }

            signal.TrySetResult();
            return GetSnapshot();
        }

        public RawMarketMessageCounters IncrementReconnect()
        {
            Interlocked.Increment(ref _reconnectCount);
            return GetSnapshot();
        }

        public RawMarketMessageCounters GetSnapshot()
        {
            var lastMessageUtcTicks = Volatile.Read(ref _lastMessageUtcTicks);
            return new RawMarketMessageCounters(
                Volatile.Read(ref _receivedComplete),
                Volatile.Read(ref _enqueued),
                Volatile.Read(ref _persisted),
                lastMessageUtcTicks == 0
                    ? null
                    : new DateTimeOffset(lastMessageUtcTicks, TimeSpan.Zero),
                Volatile.Read(ref _reconnectCount));
        }

        public async Task WaitUntilPersistedAsync(
            long target,
            CancellationToken cancellationToken)
        {
            while (Volatile.Read(ref _persisted) < target)
            {
                Task signal;
                lock (_persistedSignalLock)
                {
                    if (Volatile.Read(ref _persisted) >= target)
                        return;

                    signal = _persistedChanged.Task;
                }

                await signal.WaitAsync(cancellationToken);
            }
        }

        private static TaskCompletionSource CreateSignal() => new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        private static void UpdateMaximum(ref long target, long value)
        {
            var current = Volatile.Read(ref target);
            while (value > current)
            {
                var observed = Interlocked.CompareExchange(ref target, value, current);
                if (observed == current)
                    return;

                current = observed;
            }
        }
    }
}

internal sealed record RawMarketMessageCounters(
    long ReceivedComplete,
    long Enqueued,
    long Persisted,
    DateTimeOffset? LastMessageAt = null,
    long ReconnectCount = 0);
