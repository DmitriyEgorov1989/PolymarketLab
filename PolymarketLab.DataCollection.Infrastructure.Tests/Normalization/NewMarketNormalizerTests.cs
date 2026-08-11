using System.Text.Json;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Normalization;

public sealed class NewMarketNormalizerTests
{
    private readonly NewMarketNormalizer _normalizer = new();

    private const string ExpectedDescription =
        "This market will resolve to \"Up\" if the BNB price at the end of the time range specified in the title is greater than or equal to the price at the beginning of that range. Otherwise, it will resolve to \"Down\".\n" +
        "The resolution source for this market is information from Chainlink, specifically the BNB/USD data stream available at https://data.chain.link/streams/bnb-usd.\n" +
        "Please note that this market is about the price according to Chainlink data stream BNB/USD, not according to other sources or spot markets.";

    public static TheoryData<string, string, string> InvalidContracts => new()
    {
        { ValidJson.Replace("\"assets_ids\":[\"asset-1\",\"asset-2\"]", "\"assets_ids\":{}"), "assets_ids", "normalization.field.array.invalid" },
        { ValidJson.Replace("\"asset-2\"", "null"), "assets_ids[1]", "normalization.field.string.invalid" },
        { ValidJson.Replace("\"Down\"", "\"\""), "outcomes[1]", "normalization.field.required" },
        { ValidJson.Replace("\"event_message\":{\"id\":\"event-1\",\"ticker\":\"ticker\",\"slug\":\"event-slug\",\"title\":\"Event title\",\"description\":\"Event description\"}", "\"event_message\":[]"), "event_message", "normalization.field.object.invalid" },
        { ValidJson.Replace("\"ticker\":\"ticker\",", ""), "event_message.ticker", "normalization.field.required" },
        { ValidJson.Replace("\"rate\":\"0.02\"", "\"rate\":0.02"), "fee_schedule.rate", "normalization.field.string.invalid" },
        { ValidJson.Replace("\"taker_only\":true", "\"taker_only\":\"true\""), "fee_schedule.taker_only", "normalization.field.boolean.invalid" },
        { ValidJson.Replace("\"active\":true", "\"active\":null"), "active", "normalization.field.required" },
        { ValidJson.Replace("\"line\":\"\"", "\"line\":null"), "line", "normalization.field.required" }
    };

    private const string ValidJson = """
        {
          "id":"market-1","question":"Question?","market":"0xmarket","slug":"market-slug",
          "description":"Description","assets_ids":["asset-1","asset-2"],"outcomes":["Up","Down"],
          "event_message":{"id":"event-1","ticker":"ticker","slug":"event-slug","title":"Event title","description":"Event description"},
          "timestamp":"1785489450532","event_type":"new_market","active":true,"sports_market_type":"","line":"",
          "game_start_time":"","order_price_min_tick_size":"0.01","group_item_title":"","taker_base_fee":"0.015",
          "fees_enabled":true,"fee_schedule":{"exponent":"2","rate":"0.02","rebate_rate":"0.25","taker_only":true}
        }
        """;

    [Fact]
    public void Normalize_RealFixture_ShouldCreateConfirmedMarketAndOrderedAssets()
    {
        var result = _normalizer.Normalize(CreateRawEvent(ReadFixture("new-market.json")));

        result.Outcome.Should().Be(NormalizationOutcome.Processed);
        result.Issue.Should().BeNull();
        var normalizedEvent = result.Event!;
        normalizedEvent.RawMessageId.Should().Be(42);
        normalizedEvent.RawItemIndex.Should().Be(2);
        normalizedEvent.ProjectionVersion.Should().Be(3);
        normalizedEvent.NormalizerVersion.Should().Be(1);
        normalizedEvent.EventType.Should().Be("new_market");
        normalizedEvent.SessionId.Should().Be(CreateSessionId());
        normalizedEvent.ReceivedAt.Should().Be(DateTimeOffset.Parse("2026-08-10T10:00:00Z"));
        normalizedEvent.SourceTimestamp.Should().Be(1785489450532L);
        normalizedEvent.MarketConditionId.Should().Be("0x5c7c9447f1fbbe1c708e9000612a806d9262455955d9c6abcece540f20437284");
        normalizedEvent.AssetId.Should().BeNull();

        var market = normalizedEvent.Records[0].Should().BeOfType<NewMarketRecord>().Subject;
        market.ExternalId.Should().Be("3238697");
        market.Question.Should().Be("BNB Up or Down - August 1, 5:10AM-5:15AM ET");
        market.Slug.Should().Be("bnb-updown-5m-1785575400");
        market.Description.Should().Be(ExpectedDescription);
        market.Active.Should().BeFalse();
        market.SportsMarketType.Should().BeEmpty();
        market.Line.Should().BeNull();
        market.GameStartTime.Should().BeEmpty();
        market.OrderPriceMinTickSize.Should().Be(0.01m);
        market.GroupItemTitle.Should().BeEmpty();
        market.TakerBaseFee.Should().Be(1000m);
        market.FeesEnabled.Should().BeTrue();
        market.EventMessage.Should().Be(new NewMarketEventMessage(
            "776112",
            "bnb-updown-5m-1785575400",
            "bnb-updown-5m-1785575400",
            "BNB Up or Down - August 1, 5:10AM-5:15AM ET",
            ExpectedDescription));
        market.FeeSchedule.Should().Be(new NewMarketFeeSchedule(1m, 0.07m, 0.2m, true));

        normalizedEvent.Records.Skip(1).Should().Equal(
            new NewMarketAssetRecord(0, "53924225130593423131088866511789377621031895985470843288108815609398745612055", "Up"),
            new NewMarketAssetRecord(1, "86765292767190724373801797171330934620855632743178007916659561946345249245597", "Down"));
    }

    [Fact]
    public void Normalize_EmptyConfirmedExternalStringsAndUnknownFields_ShouldProcess()
    {
        var json = ValidJson
            .Replace("\"timestamp\":\"1785489450532\",", "")
            .Replace(
            "\"event_type\":\"new_market\"",
            "\"event_type\":\"new_market\",\"tags\":[\"ignored\"],\"condition_id\":{},\"clob_token_ids\":null,\"unknown\":true");

        var result = _normalizer.Normalize(CreateRawEvent(ParseObject(json)));

        result.Outcome.Should().Be(NormalizationOutcome.Processed);
        result.Event!.SourceTimestamp.Should().BeNull();
        var record = result.Event.Records[0].Should().BeOfType<NewMarketRecord>().Subject;
        record.SportsMarketType.Should().BeEmpty();
        record.Line.Should().BeNull();
        record.GameStartTime.Should().BeEmpty();
        record.GroupItemTitle.Should().BeEmpty();
    }

    [Fact]
    public void Normalize_NonEmptySportsLine_ShouldParseInvariantDecimal()
    {
        var json = ValidJson.Replace("\"line\":\"\"", "\"line\":\"-1.5\"");

        var result = _normalizer.Normalize(CreateRawEvent(ParseObject(json)));

        result.Outcome.Should().Be(NormalizationOutcome.Processed);
        ((NewMarketRecord)result.Event!.Records[0]).Line.Should().Be(-1.5m);
    }

    [Fact]
    public void Normalize_ArrayLengthMismatch_ShouldReturnStableInvalidWithoutPartialEvent()
    {
        var json = ValidJson.Replace("\"outcomes\":[\"Up\",\"Down\"]", "\"outcomes\":[\"Up\"]");

        var result = _normalizer.Normalize(CreateRawEvent(ParseObject(json)));

        result.Outcome.Should().Be(NormalizationOutcome.Invalid);
        result.NormalizerVersion.Should().Be(1);
        result.Event.Should().BeNull();
        result.Issue.Should().Be(new NormalizationIssue(
            "normalization.field.array.length_mismatch",
            "Outcomes must contain the same number of items as asset ids.",
            "outcomes"));
    }

    [Theory]
    [MemberData(nameof(InvalidContracts))]
    public void Normalize_WrongArrayItemOrNestedContract_ShouldReturnPathAndNoPartialEvent(
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
        _normalizer.EventType.Should().Be("new_market");
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
        var assembly = typeof(NewMarketNormalizerTests).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith($".{fileName}", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }
}
