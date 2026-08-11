using System.Text.Json;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Normalization;

public sealed class BookNormalizerTests
{
    private readonly BookNormalizer _normalizer = new();

    [Fact]
    public void Normalize_ObjectFixture_ShouldPreserveSnapshotAndAskOrder()
    {
        var result = _normalizer.Normalize(CreateRawEvent(ReadFixture("book.json")));

        result.Outcome.Should().Be(NormalizationOutcome.Processed);
        result.NormalizerVersion.Should().Be(1);
        result.Issue.Should().BeNull();
        var normalizedEvent = result.Event!;
        normalizedEvent.EventType.Should().Be("book");
        normalizedEvent.SourceTimestamp.Should().Be(1785490202355L);
        normalizedEvent.MarketConditionId.Should().Be(
            "0x69680df36dd7a982c9b18ebc0fda048ae1cf543510abe8446ab55e5403dd923e");
        normalizedEvent.AssetId.Should().Be(
            "9852683497230148976233778745433163015590012473866950809045917104353935531110");

        var snapshot = normalizedEvent.Records[0].Should()
            .BeOfType<BookSnapshotRecord>().Subject;
        snapshot.Hash.Should().Be("ded3cb54d0e28bb3efb68190f319c52cbeabc9d0");
        snapshot.TickSize.Should().BeNull();
        snapshot.LastTradePrice.Should().BeNull();

        var levels = normalizedEvent.Records.Skip(1).Cast<BookLevelRecord>().ToArray();
        levels.Should().HaveCount(43);
        levels.Should().OnlyContain(level => level.Side == OrderBookSide.Ask);
        levels.Select(level => level.LevelIndex).Should().Equal(Enumerable.Range(0, 43));
        levels.Take(3).Select(level => level.Price).Should().Equal(0.99m, 0.98m, 0.97m);
        levels[^1].Price.Should().Be(0.01m);
        levels[^1].Size.Should().Be(718163.89m);
    }

    [Fact]
    public void Normalize_ArrayFixtureEvents_ShouldSupportInitialOptionalFields()
    {
        var root = ReadFixture("book-array.json");
        var events = root.EnumerateArray().Select(item => item.Clone()).ToArray();

        var results = events
            .Select((item, index) => _normalizer.Normalize(CreateRawEvent(item, index)))
            .ToArray();

        results.Should().HaveCount(2);
        results.Should().OnlyContain(result => result.Outcome == NormalizationOutcome.Processed);
        results.Select(result => result.Event!.RawItemIndex).Should().Equal(0, 1);
        results.Select(result => (BookSnapshotRecord)result.Event!.Records[0])
            .Should().OnlyContain(snapshot =>
                snapshot.TickSize == 0.01m && snapshot.LastTradePrice.HasValue);

        foreach (var result in results)
        {
            var levels = result.Event!.Records.Skip(1).Cast<BookLevelRecord>().ToArray();
            var bids = levels.Where(level => level.Side == OrderBookSide.Bid).ToArray();
            var asks = levels.Where(level => level.Side == OrderBookSide.Ask).ToArray();
            bids.Select(level => level.LevelIndex).Should().Equal(Enumerable.Range(0, bids.Length));
            asks.Select(level => level.LevelIndex).Should().Equal(Enumerable.Range(0, asks.Length));
        }
    }

    [Fact]
    public void Normalize_EmptySides_ShouldCreateSnapshotWithoutLevels()
    {
        var json = ParseObject(
            """
            {
              "market": "market",
              "asset_id": "asset",
              "hash": "hash",
              "bids": [],
              "asks": []
            }
            """);

        var result = _normalizer.Normalize(CreateRawEvent(json));

        result.Outcome.Should().Be(NormalizationOutcome.Processed);
        result.Event!.Records.Should().ContainSingle()
            .Which.Should().BeOfType<BookSnapshotRecord>();
    }

    [Theory]
    [InlineData(
        """{"asset_id":"asset","hash":"hash","bids":[],"asks":[]}""",
        "market",
        "normalization.field.required")]
    [InlineData(
        """{"market":"market","hash":"hash","bids":[],"asks":[]}""",
        "asset_id",
        "normalization.field.required")]
    [InlineData(
        """{"market":"market","asset_id":"asset","bids":[],"asks":[]}""",
        "hash",
        "normalization.field.required")]
    [InlineData(
        """{"market":"market","asset_id":"asset","hash":"hash","timestamp":"now","bids":[],"asks":[]}""",
        "timestamp",
        "normalization.field.timestamp.invalid")]
    [InlineData(
        """{"market":"market","asset_id":"asset","hash":"hash","tick_size":0.01,"bids":[],"asks":[]}""",
        "tick_size",
        "normalization.field.string.invalid")]
    public void Normalize_InvalidHeaderField_ShouldReturnInvalid(
        string json,
        string expectedField,
        string expectedCode)
    {
        var result = _normalizer.Normalize(CreateRawEvent(ParseObject(json)));

        AssertInvalid(result, expectedCode, expectedField);
    }

    [Theory]
    [InlineData("bids", "null", "normalization.field.required")]
    [InlineData("bids", "{}", "normalization.field.array.invalid")]
    [InlineData("asks", "null", "normalization.field.required")]
    [InlineData("asks", "\"levels\"", "normalization.field.array.invalid")]
    public void Normalize_InvalidSideShape_ShouldReturnInvalid(
        string field,
        string value,
        string expectedCode)
    {
        var bids = field == "bids" ? value : "[]";
        var asks = field == "asks" ? value : "[]";
        var json = ParseObject(
            $"{{\"market\":\"market\",\"asset_id\":\"asset\",\"hash\":\"hash\",\"bids\":{bids},\"asks\":{asks}}}");

        var result = _normalizer.Normalize(CreateRawEvent(json));

        AssertInvalid(result, expectedCode, field);
    }

    [Fact]
    public void Normalize_MissingSide_ShouldReturnRequiredIssue()
    {
        var json = ParseObject(
            """{"market":"market","asset_id":"asset","hash":"hash","asks":[]}""");

        var result = _normalizer.Normalize(CreateRawEvent(json));

        AssertInvalid(result, "normalization.field.required", "bids");
    }

    [Fact]
    public void Normalize_ErrorInSecondLevel_ShouldInvalidateWholeEventWithIndexedPath()
    {
        var json = ParseObject(
            """
            {
              "market": "market",
              "asset_id": "asset",
              "hash": "hash",
              "bids": [
                {"price":"0.1","size":"1"},
                {"price":"invalid","size":"2"}
              ],
              "asks": []
            }
            """);

        var result = _normalizer.Normalize(CreateRawEvent(json));

        AssertInvalid(result, "normalization.field.decimal.invalid", "bids[1].price");
        result.Event.Should().BeNull();
    }

    [Fact]
    public void Normalize_NonObjectLevel_ShouldReturnIndexedPath()
    {
        var json = ParseObject(
            """
            {
              "market": "market",
              "asset_id": "asset",
              "hash": "hash",
              "bids": [],
              "asks": [{"price":"0.9","size":"1"}, null]
            }
            """);

        var result = _normalizer.Normalize(CreateRawEvent(json));

        AssertInvalid(result, "normalization.field.object.invalid", "asks[1]");
    }

    [Theory]
    [InlineData("price", "-0.01", "normalization.field.range.invalid")]
    [InlineData("price", "1.01", "normalization.field.range.invalid")]
    [InlineData("price", "invalid", "normalization.field.decimal.invalid")]
    [InlineData("size", "-0.01", "normalization.field.range.invalid")]
    [InlineData("size", "invalid", "normalization.field.decimal.invalid")]
    public void Normalize_InvalidLevelValue_ShouldReturnNestedFieldPath(
        string field,
        string value,
        string expectedCode)
    {
        var price = field == "price" ? value : "0.5";
        var size = field == "size" ? value : "1";
        var json = ParseObject(
            $"{{\"market\":\"market\",\"asset_id\":\"asset\",\"hash\":\"hash\",\"bids\":[],\"asks\":[{{\"price\":\"{price}\",\"size\":\"{size}\"}}]}} ");

        var result = _normalizer.Normalize(CreateRawEvent(json));

        AssertInvalid(result, expectedCode, $"asks[0].{field}");
    }

    [Theory]
    [InlineData("tick_size", "0", "normalization.field.range.invalid")]
    [InlineData("tick_size", "invalid", "normalization.field.decimal.invalid")]
    [InlineData("last_trade_price", "-0.01", "normalization.field.range.invalid")]
    [InlineData("last_trade_price", "1.01", "normalization.field.range.invalid")]
    public void Normalize_InvalidOptionalFinancialField_ShouldReturnInvalid(
        string field,
        string value,
        string expectedCode)
    {
        var json = ParseObject(
            $"{{\"market\":\"market\",\"asset_id\":\"asset\",\"hash\":\"hash\",\"bids\":[],\"asks\":[],\"{field}\":\"{value}\"}} ");

        var result = _normalizer.Normalize(CreateRawEvent(json));

        AssertInvalid(result, expectedCode, field);
    }

    [Fact]
    public void Normalize_NullOptionalAndUnknownFields_ShouldProcess()
    {
        var json = ParseObject(
            """
            {
              "market": "market",
              "asset_id": "asset",
              "hash": "hash",
              "timestamp": null,
              "tick_size": null,
              "last_trade_price": null,
              "bids": [{"price":"0","size":"0","unknown":true}],
              "asks": [{"price":"1","size":"0"}],
              "unknown_root": [1, 2]
            }
            """);

        var result = _normalizer.Normalize(CreateRawEvent(json));

        result.Outcome.Should().Be(NormalizationOutcome.Processed);
        result.Event!.SourceTimestamp.Should().BeNull();
        var snapshot = (BookSnapshotRecord)result.Event.Records[0];
        snapshot.TickSize.Should().BeNull();
        snapshot.LastTradePrice.Should().BeNull();
    }

    [Fact]
    public void Contract_ShouldDeclareExpectedEventTypeAndVersion()
    {
        _normalizer.EventType.Should().Be("book");
        _normalizer.Version.Should().Be(1);
    }

    private static void AssertInvalid(
        NormalizationResult result,
        string expectedCode,
        string expectedField)
    {
        result.RawItemIndex.Should().Be(2);
        result.Outcome.Should().Be(NormalizationOutcome.Invalid);
        result.NormalizerVersion.Should().Be(1);
        result.Event.Should().BeNull();
        result.Issue.Should().NotBeNull();
        result.Issue!.Code.Should().Be(expectedCode);
        result.Issue.Field.Should().Be(expectedField);
    }

    private static LogicalRawEvent CreateRawEvent(JsonElement json, int rawItemIndex = 2)
    {
        return new LogicalRawEvent(
            rawMessageId: 42,
            rawItemIndex: rawItemIndex,
            projectionVersion: 3,
            sessionId: CreateSessionId(),
            receivedAt: DateTimeOffset.Parse("2026-08-10T10:00:00Z"),
            json: json);
    }

    private static CollectorSessionId CreateSessionId()
    {
        return CollectorSessionId.Create(
            Guid.Parse("6d9ac447-7bcc-4c85-8619-0384da429a33")).Value;
    }

    private static JsonElement ParseObject(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonElement ReadFixture(string fileName)
    {
        var assembly = typeof(BookNormalizerTests).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith($".{fileName}", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }
}
