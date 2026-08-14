using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.Normalization;

[Collection(NormalizerTelemetryCollection.Name)]
public sealed class NormalizerTelemetryTests
{
    [Fact]
    public void RecordBatch_ShouldPublishOutcomeCountersAndDurationWithBoundedTags()
    {
        var measurements = new ConcurrentBag<MetricMeasurement>();
        using var listener = CreateListener(measurements);
        using var telemetry = new NormalizerTelemetry();

        telemetry.RecordBatch(
            3,
            new NormalizationBatchResult(10, 4, 3, 2, 1, 1, 10),
            TimeSpan.FromMilliseconds(12.5));

        measurements.Should().ContainEquivalentOf(new MetricMeasurement(
            "normalizer_messages_processed", 4, Tags(3)));
        measurements.Should().ContainEquivalentOf(new MetricMeasurement(
            "normalizer_messages_invalid", 3, Tags(3)));
        measurements.Should().ContainEquivalentOf(new MetricMeasurement(
            "normalizer_messages_unsupported", 2, Tags(3)));
        measurements.Should().ContainEquivalentOf(new MetricMeasurement(
            "normalizer_messages_failed", 1, Tags(3)));
        measurements.Should().ContainEquivalentOf(new MetricMeasurement(
            "normalizer_batches", 1, Tags(3)));
        measurements.Should().ContainEquivalentOf(new MetricMeasurement(
            "normalizer_batch_duration_ms", 12.5, Tags(3)));
        measurements.Should().OnlyContain(measurement =>
            measurement.Tags.Keys.SequenceEqual(new[] { "projection_version" }));
    }

    [Fact]
    public void BacklogGauges_ShouldPublishOnlyLatestVersionedSnapshot()
    {
        var measurements = new ConcurrentBag<MetricMeasurement>();
        using var listener = CreateListener(measurements);
        using var telemetry = new NormalizerTelemetry();

        listener.RecordObservableInstruments();
        measurements.Should().BeEmpty();

        telemetry.UpdateBacklog(new NormalizationBacklogSnapshot(2, 5, 7));
        listener.RecordObservableInstruments();

        measurements.Should().ContainEquivalentOf(new MetricMeasurement(
            "normalizer_pending_messages", 5, Tags(2)));
        measurements.Should().ContainEquivalentOf(new MetricMeasurement(
            "normalizer_lag_messages", 7, Tags(2)));
        measurements.Should().OnlyContain(measurement =>
            measurement.Tags.Keys.SequenceEqual(new[] { "projection_version" }));
    }

    [Fact]
    public void RecordBatch_EmptyPollingResult_ShouldNotPublishBatchMetrics()
    {
        var measurements = new ConcurrentBag<MetricMeasurement>();
        using var listener = CreateListener(measurements);
        using var telemetry = new NormalizerTelemetry();

        telemetry.RecordBatch(
            1,
            new NormalizationBatchResult(0, 0, 0, 0, 0, null, null),
            TimeSpan.FromMilliseconds(1));

        measurements.Should().BeEmpty();
    }

    private static MeterListener CreateListener(ConcurrentBag<MetricMeasurement> measurements)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == NormalizerTelemetry.MeterName)
                    meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add(new MetricMeasurement(
                instrument.Name,
                value,
                tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value))));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add(new MetricMeasurement(
                instrument.Name,
                value,
                tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value))));
        listener.Start();
        return listener;
    }

    private static IReadOnlyDictionary<string, object?> Tags(int projectionVersion) =>
        new Dictionary<string, object?> { ["projection_version"] = projectionVersion };

    private sealed record MetricMeasurement(
        string Name,
        double Value,
        IReadOnlyDictionary<string, object?> Tags);
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NormalizerTelemetryCollection
{
    public const string Name = "Normalizer telemetry";
}
