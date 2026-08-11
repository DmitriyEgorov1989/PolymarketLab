using System.Text.Json;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Normalization;

public sealed class TickSizeChangeNormalizerTests
{
    private readonly TickSizeChangeNormalizer _normalizer = new();

    [Fact]
    public void Normalize_RealFixture_ShouldPreserveDecimalValuesAndHeader()
    {
        var result = _normalizer.Normalize(CreateRawEvent(ReadFixture("tick-size-change.json")));

        result.Outcome.Should().Be(NormalizationOutcome.Processed);
        result.NormalizerVersion.Should().Be(1);
        var normalizedEvent = result.Event!;
        normalizedEvent.EventType.Should().Be("tick_size_change");
        normalizedEvent.SourceTimestamp.Should().Be(1786349781405L);
        normalizedEvent.AssetId.Should().Be(
            "39380455732777541460182228901170103342295047760602489732172685203069049658354");
        var record = normalizedEvent.Records.Should().ContainSingle().Which
            .Should().BeOfType<TickSizeChangeRecord>().Subject;
        record.OldTickSize.Should().Be(0.01m);
        record.NewTickSize.Should().Be(0.001m);
    }

    [Theory]
    [InlineData("old_tick_size", "invalid", "normalization.field.decimal.invalid")]
    [InlineData("new_tick_size", "invalid", "normalization.field.decimal.invalid")]
    [InlineData("new_tick_size", "0", "normalization.field.range.invalid")]
    [InlineData("new_tick_size", "-0.001", "normalization.field.range.invalid")]
    public void Normalize_InvalidTickSize_ShouldReturnInvalid(
        string field,
        string value,
        string expectedCode)
    {
        var oldTickSize = field == "old_tick_size" ? value : "0.01";
        var newTickSize = field == "new_tick_size" ? value : "0.001";
        var json = ParseObject(
            $"{{\"market\":\"market\",\"asset_id\":\"asset\",\"old_tick_size\":\"{oldTickSize}\",\"new_tick_size\":\"{newTickSize}\"}}");

        var result = _normalizer.Normalize(CreateRawEvent(json));

        AssertInvalid(result, expectedCode, field);
    }

    [Fact]
    public void Normalize_HighPrecisionValues_ShouldNotLosePrecision()
    {
        var json = ParseObject(
            """{"market":"market","asset_id":"asset","old_tick_size":"0.1234567890123456789012345678","new_tick_size":"0.0000000000000000000000000001"}""");

        var result = _normalizer.Normalize(CreateRawEvent(json));

        var record = (TickSizeChangeRecord)result.Event!.Records.Single();
        record.OldTickSize.Should().Be(0.1234567890123456789012345678m);
        record.NewTickSize.Should().Be(0.0000000000000000000000000001m);
    }

    [Fact]
    public void Normalize_MissingRequiredField_ShouldReturnInvalid()
    {
        var json = ParseObject(
            """{"market":"market","asset_id":"asset","old_tick_size":"0.01"}""");

        var result = _normalizer.Normalize(CreateRawEvent(json));

        AssertInvalid(result, "normalization.field.required", "new_tick_size");
    }

    [Fact]
    public void Normalize_MissingTimestampAndUnknownField_ShouldProcess()
    {
        var json = ParseObject(
            """{"market":"market","asset_id":"asset","old_tick_size":"0","new_tick_size":"0.01","unknown":true}""");

        var result = _normalizer.Normalize(CreateRawEvent(json));

        result.Outcome.Should().Be(NormalizationOutcome.Processed);
        result.Event!.SourceTimestamp.Should().BeNull();
        ((TickSizeChangeRecord)result.Event.Records.Single()).OldTickSize.Should().Be(0m);
    }

    [Fact]
    public void Contract_ShouldDeclareExpectedEventTypeAndVersion()
    {
        _normalizer.EventType.Should().Be("tick_size_change");
        _normalizer.Version.Should().Be(1);
    }

    private static void AssertInvalid(
        NormalizationResult result,
        string expectedCode,
        string expectedField)
    {
        result.Outcome.Should().Be(NormalizationOutcome.Invalid);
        result.NormalizerVersion.Should().Be(1);
        result.Event.Should().BeNull();
        result.Issue!.Code.Should().Be(expectedCode);
        result.Issue.Field.Should().Be(expectedField);
    }

    private static LogicalRawEvent CreateRawEvent(JsonElement json)
    {
        return new LogicalRawEvent(
            42,
            2,
            3,
            CollectorSessionId.Create(Guid.Parse("6d9ac447-7bcc-4c85-8619-0384da429a33")).Value,
            DateTimeOffset.Parse("2026-08-10T10:00:00Z"),
            json);
    }

    private static JsonElement ParseObject(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonElement ReadFixture(string fileName)
    {
        var assembly = typeof(TickSizeChangeNormalizerTests).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith($".{fileName}", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }
}
