using PolymarketLab.SharedKernel.DomainModels.Ids;
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
    private readonly ConcurrentDictionary<CollectorSessionId, CounterState> _states = new();

    public RawMarketMessageTelemetry()
    {
        _receivedCompleteCounter = _meter.CreateCounter<long>(
            "raw_messages.received_complete");
        _enqueuedCounter = _meter.CreateCounter<long>("raw_messages.enqueued");
        _persistedCounter = _meter.CreateCounter<long>("raw_messages.persisted");
    }

    public RawMarketMessageCounters RecordReceivedComplete(
        CollectorSessionId sessionId)
    {
        _receivedCompleteCounter.Add(1, CreateTags(sessionId));
        return _states.GetOrAdd(sessionId, _ => new CounterState())
            .IncrementReceivedComplete();
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

    public RawMarketMessageCounters GetSnapshot(CollectorSessionId sessionId)
    {
        return _states.TryGetValue(sessionId, out var state)
            ? state.GetSnapshot()
            : new RawMarketMessageCounters(0, 0, 0);
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

        public RawMarketMessageCounters IncrementReceivedComplete()
        {
            Interlocked.Increment(ref _receivedComplete);
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
            return GetSnapshot();
        }

        public RawMarketMessageCounters GetSnapshot()
        {
            return new RawMarketMessageCounters(
                Volatile.Read(ref _receivedComplete),
                Volatile.Read(ref _enqueued),
                Volatile.Read(ref _persisted));
        }
    }
}

internal sealed record RawMarketMessageCounters(
    long ReceivedComplete,
    long Enqueued,
    long Persisted);
