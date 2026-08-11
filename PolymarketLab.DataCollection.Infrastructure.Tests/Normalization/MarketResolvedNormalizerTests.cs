using System.Text.Json;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Normalization;

public sealed class MarketResolvedNormalizerTests
{
    private readonly MarketResolvedNormalizer _normalizer = new();

    private const string ValidJson = """
        {
          "id":"3440615","market":"0xmarket","assets_ids":["asset-1","asset-2"],
          "winning_asset_id":"asset-1","winning_outcome":"Up","event_message":null,
          "timestamp":"1786349854358","event_type":"market_resolved"
        }
        """;

    public static TheoryData<string, string, string> InvalidArrays => new()
    {
        { ValidJson.Replace("\"assets_ids\":[\"asset-1\",\"asset-2\"],", ""), "assets_ids", "normalization.field.required" },
        { ValidJson.Replace("[\"asset-1\",\"asset-2\"]", "null"), "assets_ids", "normalization.field.required" },
        { ValidJson.Replace("[\"asset-1\",\"asset-2\"]", "{}"), "assets_ids", "normalization.field.array.invalid" },
        { ValidJson.Replace("[\"asset-1\",\"asset-2\"]", "[]"), "assets_ids", "normalization.field.array.empty" },
        { ValidJson.Replace("\"asset-2\"", "null"), "assets_ids[1]", "normalization.field.string.invalid" },
        { ValidJson.Replace("\"asset-2\"", "\"\""), "assets_ids[1]", "normalization.field.required" }
    };

    public static TheoryData<string> MissingWinners => new()
    {
        ValidJson.Replace("\"winning_asset_id\":\"asset-1\",", ""),
        ValidJson.Replace("\"winning_asset_id\":\"asset-1\"", "\"winning_asset_id\":null"),
        ValidJson.Replace("\"winning_asset_id\":\"asset-1\"", "\"winning_asset_id\":\"\""),
        ValidJson.Replace("\"winning_outcome\":\"Up\",", ""),
        ValidJson.Replace("\"winning_outcome\":\"Up\"", "\"winning_outcome\":null"),
        ValidJson.Replace("\"winning_outcome\":\"Up\"", "\"winning_outcome\":\"\"")
    };

    [Fact]
    public void Normalize_RealFixture_ShouldCreateExactHeaderResolutionAndOrderedAssets()
    {
        var result = _normalizer.Normalize(CreateRawEvent(ReadFixture("market-resolved.json")));

        result.Outcome.Should().Be(NormalizationOutcome.Processed);
        result.Issue.Should().BeNull();
        var normalizedEvent = result.Event!;
        normalizedEvent.RawMessageId.Should().Be(42);
        normalizedEvent.RawItemIndex.Should().Be(2);
        normalizedEvent.ProjectionVersion.Should().Be(3);
        normalizedEvent.NormalizerVersion.Should().Be(1);
        normalizedEvent.EventType.Should().Be("market_resolved");
        normalizedEvent.SessionId.Should().Be(CreateSessionId());
        normalizedEvent.ReceivedAt.Should().Be(DateTimeOffset.Parse("2026-08-10T10:00:00Z"));
        normalizedEvent.SourceTimestamp.Should().Be(1786349854358L);
        normalizedEvent.MarketConditionId.Should().Be("0xdd306d515bd45284b15076a703f63217ca90d56a4a0711fa02a7565c7384bcce");
        normalizedEvent.AssetId.Should().BeNull();
        normalizedEvent.Records.Should().Equal(
            new MarketResolvedRecord(
                "3440615",
                "39380455732777541460182228901170103342295047760602489732172685203069049658354",
                "Up"),
            new MarketResolvedAssetRecord(
                0,
                "39380455732777541460182228901170103342295047760602489732172685203069049658354"),
            new MarketResolvedAssetRecord(
                1,
                "111829523372964714082931288140246517573844643533796580245290450692807668293921"));
    }

    [Fact]
    public void Normalize_NullEventMessageOptionalTimestampUnknownFieldsAndNonMemberWinner_ShouldProcess()
    {
        var json = ValidJson
            .Replace("\"timestamp\":\"1786349854358\",", "")
            .Replace("\"winning_asset_id\":\"asset-1\"", "\"winning_asset_id\":\"different-big-999999999999999999999999999999999999999\"")
            .Replace("\"event_type\":\"market_resolved\"", "\"event_type\":\"market_resolved\",\"tags\":[\"ignored\"],\"unknown\":{}");

        var result = _normalizer.Normalize(CreateRawEvent(ParseObject(json)));

        result.Outcome.Should().Be(NormalizationOutcome.Processed);
        result.Event!.SourceTimestamp.Should().BeNull();
        ((MarketResolvedRecord)result.Event.Records[0]).WinningAssetId.Should()
            .Be("different-big-999999999999999999999999999999999999999");
    }

    [Theory]
    [MemberData(nameof(MissingWinners))]
    public void Normalize_MissingNullOrEmptyWinner_ShouldReturnInvalidWithoutEvent(string json)
    {
        var result = _normalizer.Normalize(CreateRawEvent(ParseObject(json)));

        result.Outcome.Should().Be(NormalizationOutcome.Invalid);
        result.NormalizerVersion.Should().Be(1);
        result.Event.Should().BeNull();
        result.Issue!.Code.Should().Be("normalization.field.required");
    }

    [Theory]
    [MemberData(nameof(InvalidArrays))]
    public void Normalize_InvalidAssetArray_ShouldReturnIndexedIssueWithoutPartialEvent(
        string json,
        string expectedField,
        string expectedCode)
    {
        var result = _normalizer.Normalize(CreateRawEvent(ParseObject(json)));

        result.Outcome.Should().Be(NormalizationOutcome.Invalid);
        result.NormalizerVersion.Should().Be(1);
        result.Event.Should().BeNull();
        result.Issue!.Field.Should().Be(expectedField);
        result.Issue.Code.Should().Be(expectedCode);
    }

    [Fact]
    public void Contract_ShouldDeclareExpectedEventTypeAndVersion()
    {
        _normalizer.EventType.Should().Be("market_resolved");
        _normalizer.Version.Should().Be(1);
    }

    private static LogicalRawEvent CreateRawEvent(JsonElement json)
    {
        return new LogicalRawEvent(
            42,
            2,
            3,
            CreateSessionId(),
            DateTimeOffset.Parse("2026-08-10T10:00:00Z"),
            json);
    }

    private static CollectorSessionId CreateSessionId() => CollectorSessionId.Create(
        Guid.Parse("6d9ac447-7bcc-4c85-8619-0384da429a33")).Value;

    private static JsonElement ParseObject(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonElement ReadFixture(string fileName)
    {
        var assembly = typeof(MarketResolvedNormalizerTests).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith($".{fileName}", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }
}
