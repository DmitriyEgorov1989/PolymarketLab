using System.Text.Json;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Normalization;

public sealed class BestBidAskNormalizerTests
{
    private readonly BestBidAskNormalizer _normalizer = new();

    [Fact]
    public void Normalize_RealFixture_ShouldPreserveBoundaryValues()
    {
        var result = _normalizer.Normalize(CreateRawEvent(ReadFixture("best-bid-ask.json")));

        result.Outcome.Should().Be(NormalizationOutcome.Processed);
        result.NormalizerVersion.Should().Be(1);
        var normalizedEvent = result.Event!;
        normalizedEvent.EventType.Should().Be("best_bid_ask");
        normalizedEvent.SourceTimestamp.Should().Be(1786349854331L);
        var record = normalizedEvent.Records.Should().ContainSingle().Which
            .Should().BeOfType<BestBidAskRecord>().Subject;
        record.BestBid.Should().Be(0m);
        record.BestAsk.Should().Be(1m);
        record.Spread.Should().Be(1m);
    }

    [Theory]
    [InlineData("best_bid")]
    [InlineData("best_ask")]
    [InlineData("spread")]
    public void Normalize_MissingConfirmedField_ShouldReturnInvalid(string missingField)
    {
        var fields = new Dictionary<string, string>
        {
            ["best_bid"] = "\"0\"",
            ["best_ask"] = "\"1\"",
            ["spread"] = "\"1\""
        };
        fields.Remove(missingField);
        var values = string.Join(",", fields.Select(pair => $"\"{pair.Key}\":{pair.Value}"));
        var json = ParseObject($"{{\"market\":\"market\",\"asset_id\":\"asset\",{values}}}");

        var result = _normalizer.Normalize(CreateRawEvent(json));

        AssertInvalid(result, "normalization.field.required", missingField);
    }

    [Theory]
    [InlineData("best_bid", "-0.001")]
    [InlineData("best_bid", "1.001")]
    [InlineData("best_ask", "-0.001")]
    [InlineData("best_ask", "1.001")]
    [InlineData("spread", "-0.001")]
    public void Normalize_ValueOutsideSupportedRange_ShouldReturnInvalid(string field, string value)
    {
        var bid = field == "best_bid" ? value : "0";
        var ask = field == "best_ask" ? value : "1";
        var spread = field == "spread" ? value : "1";
        var json = ParseObject(
            $"{{\"market\":\"market\",\"asset_id\":\"asset\",\"best_bid\":\"{bid}\",\"best_ask\":\"{ask}\",\"spread\":\"{spread}\"}}");

        var result = _normalizer.Normalize(CreateRawEvent(json));

        AssertInvalid(result, "normalization.field.range.invalid", field);
    }

    [Fact]
    public void Normalize_HighPrecisionValues_ShouldNotLosePrecisionOrRecalculateSpread()
    {
        var json = ParseObject(
            """{"market":"market","asset_id":"asset","best_bid":"0.1234567890123456789012345678","best_ask":"0.9876543210987654321098765432","spread":"0.1111111111111111111111111111"}""");

        var result = _normalizer.Normalize(CreateRawEvent(json));

        var record = (BestBidAskRecord)result.Event!.Records.Single();
        record.BestBid.Should().Be(0.1234567890123456789012345678m);
        record.BestAsk.Should().Be(0.9876543210987654321098765432m);
        record.Spread.Should().Be(0.1111111111111111111111111111m);
    }

    [Fact]
    public void Contract_ShouldDeclareExpectedEventTypeAndVersion()
    {
        _normalizer.EventType.Should().Be("best_bid_ask");
        _normalizer.Version.Should().Be(1);
    }

    private static void AssertInvalid(NormalizationResult result, string code, string field)
    {
        result.Outcome.Should().Be(NormalizationOutcome.Invalid);
        result.Event.Should().BeNull();
        result.NormalizerVersion.Should().Be(1);
        result.Issue!.Code.Should().Be(code);
        result.Issue.Field.Should().Be(field);
    }

    private static LogicalRawEvent CreateRawEvent(JsonElement json)
    {
        return new LogicalRawEvent(
            42, 2, 3,
            CollectorSessionId.Create(Guid.Parse("6d9ac447-7bcc-4c85-8619-0384da429a33")).Value,
            DateTimeOffset.Parse("2026-08-10T10:00:00Z"), json);
    }

    private static JsonElement ParseObject(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonElement ReadFixture(string fileName)
    {
        var assembly = typeof(BestBidAskNormalizerTests).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith($".{fileName}", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }
}
