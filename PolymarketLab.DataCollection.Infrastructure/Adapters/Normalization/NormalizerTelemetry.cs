using System.Diagnostics;
using System.Diagnostics.Metrics;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;

internal sealed class NormalizerTelemetry : IDisposable
{
    public const string MeterName = "PolymarketLab.DataCollection.Normalizer";

    private readonly Meter meter = new(MeterName, "1.0.0");
    private readonly Counter<long> processed;
    private readonly Counter<long> invalid;
    private readonly Counter<long> unsupported;
    private readonly Counter<long> failed;
    private readonly Counter<long> batches;
    private readonly Histogram<double> batchDuration;
    private NormalizationBacklogSnapshot? backlog;

    public NormalizerTelemetry()
    {
        processed = meter.CreateCounter<long>("normalizer_messages_processed");
        invalid = meter.CreateCounter<long>("normalizer_messages_invalid");
        unsupported = meter.CreateCounter<long>("normalizer_messages_unsupported");
        failed = meter.CreateCounter<long>("normalizer_messages_failed");
        batches = meter.CreateCounter<long>("normalizer_batches");
        batchDuration = meter.CreateHistogram<double>("normalizer_batch_duration_ms");
        meter.CreateObservableGauge<long>(
            "normalizer_pending_messages",
            ObservePendingMessages);
        meter.CreateObservableGauge<long>(
            "normalizer_lag_messages",
            ObserveLagMessages);
    }

    public void RecordBatch(
        int projectionVersion,
        NormalizationBatchResult result,
        TimeSpan duration)
    {
        if (result.Total == 0)
            return;

        var tags = CreateTags(projectionVersion);
        if (result.Processed > 0)
            processed.Add(result.Processed, tags);
        if (result.Invalid > 0)
            invalid.Add(result.Invalid, tags);
        if (result.Unsupported > 0)
            unsupported.Add(result.Unsupported, tags);
        if (result.Failed > 0)
            failed.Add(result.Failed, tags);
        batches.Add(1, tags);
        batchDuration.Record(duration.TotalMilliseconds, tags);
    }

    public void UpdateBacklog(NormalizationBacklogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Volatile.Write(ref backlog, snapshot);
    }

    public void Dispose() => meter.Dispose();

    private IEnumerable<Measurement<long>> ObservePendingMessages()
    {
        var snapshot = Volatile.Read(ref backlog);
        return snapshot is null
            ? []
            : [new Measurement<long>(
                snapshot.PendingMessages,
                CreateTags(snapshot.ProjectionVersion))];
    }

    private IEnumerable<Measurement<long>> ObserveLagMessages()
    {
        var snapshot = Volatile.Read(ref backlog);
        return snapshot is null
            ? []
            : [new Measurement<long>(
                snapshot.LagMessages,
                CreateTags(snapshot.ProjectionVersion))];
    }

    private static TagList CreateTags(int projectionVersion) =>
        new() { { "projection_version", projectionVersion } };
}

internal sealed record NormalizationBacklogSnapshot(
    int ProjectionVersion,
    long PendingMessages,
    long LagMessages);
