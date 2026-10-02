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
    public void RecordBatch_WithPhaseDurations_ShouldPublishBoundedPhaseMetrics()
    {
        var measurements = new ConcurrentBag<MetricMeasurement>();
        using var listener = CreateListener(measurements);
        using var telemetry = new NormalizerTelemetry();

        telemetry.RecordBatch(
            3,
            new NormalizationBatchResult(
                2,
                2,
                0,
                0,
                0,
                1,
                2,
                durations: new NormalizationPhaseDurations(
                    TimeSpan.FromMilliseconds(1),
                    TimeSpan.FromMilliseconds(2),
                    TimeSpan.FromMilliseconds(3))),
            TimeSpan.FromMilliseconds(6));

        measurements.Where(item => item.Name == "normalizer_phase_duration_ms")
            .Should().BeEquivalentTo(
            [
                new MetricMeasurement("normalizer_phase_duration_ms", 1, PhaseTags(3, "claim")),
                new MetricMeasurement("normalizer_phase_duration_ms", 2, PhaseTags(3, "build")),
                new MetricMeasurement("normalizer_phase_duration_ms", 3, PhaseTags(3, "write"))
            ]);
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

    [Fact]
    public void RecordBatch_EmptyMeasuredPollingResult_ShouldPublishOnlyClaimPhase()
    {
        var measurements = new ConcurrentBag<MetricMeasurement>();
        using var listener = CreateListener(measurements);
        using var telemetry = new NormalizerTelemetry();

        telemetry.RecordBatch(
            1,
            new NormalizationBatchResult(
                0,
                0,
                0,
                0,
                0,
                null,
                null,
                durations: new NormalizationPhaseDurations(
                    TimeSpan.FromMilliseconds(1),
                    TimeSpan.Zero,
                    TimeSpan.Zero)),
            TimeSpan.FromMilliseconds(1));

        measurements.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new MetricMeasurement(
                "normalizer_phase_duration_ms",
                1,
                PhaseTags(1, "claim")));
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

    private static IReadOnlyDictionary<string, object?> PhaseTags(
        int projectionVersion,
        string phase) =>
        new Dictionary<string, object?>
        {
            ["projection_version"] = projectionVersion,
            ["phase"] = phase
        };

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
