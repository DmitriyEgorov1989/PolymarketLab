using System.Text.Json;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Normalization;

public sealed class PriceChangeNormalizerTests
{
    private readonly PriceChangeNormalizer _normalizer = new();

    public static TheoryData<string, string, string> InvalidItemFields => new()
    {
        {
            """{"price":"0.5","size":"1","side":"BUY"}""",
            "price_changes[0].asset_id",
            "normalization.field.required"
        },
        {
            """{"asset_id":"asset","price":0.5,"size":"1","side":"BUY"}""",
            "price_changes[0].price",
            "normalization.field.string.invalid"
        },
        {
            """{"asset_id":"asset","price":"0.5","side":"BUY"}""",
            "price_changes[0].size",
            "normalization.field.required"
        },
        {
            """{"asset_id":"asset","price":"0.5","size":"-0.01","side":"BUY"}""",
            "price_changes[0].size",
            "normalization.field.range.invalid"
        },
        {
            """{"asset_id":"asset","price":"0.5","size":"1","side":"buy"}""",
            "price_changes[0].side",
            "normalization.field.trade_side.invalid"
        },
        {
            """{"asset_id":"asset","price":"0.5","size":"1","side":"BUY","hash":1}""",
            "price_changes[0].hash",
            "normalization.field.string.invalid"
        },
        {
            """{"asset_id":"asset","price":"0.5","size":"1","side":"BUY","best_bid":"1.01"}""",
            "price_changes[0].best_bid",
            "normalization.field.range.invalid"
        },
        {
            """{"asset_id":"asset","price":"0.5","size":"1","side":"BUY","best_ask":"-0.01"}""",
            "price_changes[0].best_ask",
            "normalization.field.range.invalid"
        }
    };

    [Fact]
    public void Normalize_RealFixture_ShouldCreateTwoRecordsInSourceOrder()
    {
        var rawEvent = CreateRawEvent(ReadFixture("price-change.json"));

        var result = _normalizer.Normalize(rawEvent);

        result.Outcome.Should().Be(NormalizationOutcome.Processed);
        result.NormalizerVersion.Should().Be(1);
        result.Issue.Should().BeNull();
        var normalizedEvent = result.Event!;
        normalizedEvent.RawMessageId.Should().Be(42);
        normalizedEvent.RawItemIndex.Should().Be(2);
        normalizedEvent.ProjectionVersion.Should().Be(3);
        normalizedEvent.NormalizerVersion.Should().Be(1);
        normalizedEvent.EventType.Should().Be("price_change");
        normalizedEvent.SessionId.Should().Be(CreateSessionId());
        normalizedEvent.ReceivedAt.Should().Be(DateTimeOffset.Parse("2026-08-10T10:00:00Z"));
        normalizedEvent.SourceTimestamp.Should().Be(1786349854329L);
        normalizedEvent.MarketConditionId.Should().Be(
            "0xdd306d515bd45284b15076a703f63217ca90d56a4a0711fa02a7565c7384bcce");
        normalizedEvent.AssetId.Should().BeNull();

        var records = normalizedEvent.Records.Cast<PriceChangeRecord>().ToArray();
        records.Should().HaveCount(2);
        records.Select(record => record.ItemIndex).Should().Equal(0, 1);
        records.Select(record => record.AssetId).Should().Equal(
            "39380455732777541460182228901170103342295047760602489732172685203069049658354",
            "111829523372964714082931288140246517573844643533796580245290450692807668293921");
        records.Select(record => record.Price).Should().Equal(0.1m, 0.9m);
        records.Select(record => record.Side).Should().Equal(TradeSide.Buy, TradeSide.Sell);
        records.Select(record => record.Size).Should().Equal(0m, 0m);
        records.Select(record => record.Hash).Should().Equal(
            "757d5c16f528557f9f563d167b5ca79b5bac7a7c",
            "46eb4059bf9b73848bd3a16e308b1f37a23660d3");
        records.Select(record => record.BestBid).Should().Equal(0m, 0m);
        records.Select(record => record.BestAsk).Should().Equal(1m, 1m);
    }

    [Theory]
    [InlineData("{\"market\":\"market\"}", "normalization.field.required")]
    [InlineData(
        "{\"market\":\"market\",\"price_changes\":null}",
        "normalization.field.required")]
    [InlineData(
        "{\"market\":\"market\",\"price_changes\":{}}",
        "normalization.field.array.invalid")]
    [InlineData(
        "{\"market\":\"market\",\"price_changes\":\"items\"}",
        "normalization.field.array.invalid")]
    public void Normalize_InvalidArrayShape_ShouldReturnInvalid(
        string json,
        string expectedCode)
    {
        var result = _normalizer.Normalize(CreateRawEvent(ParseObject(json)));

        AssertInvalid(result, expectedCode, "price_changes");
    }

    [Fact]
    public void Normalize_EmptyArray_ShouldReturnDefinedInvalidResult()
    {
        var result = _normalizer.Normalize(CreateRawEvent(ParseObject(
            """{"market":"market","price_changes":[]}""")));

        AssertInvalid(
            result,
            "normalization.field.array.empty",
            "price_changes");
    }

    [Fact]
    public void Normalize_ErrorInSecondItem_ShouldInvalidateWholeEventWithoutPartialRecords()
    {
        var json = ParseObject(
            """
            {
              "market": "market",
              "price_changes": [
                {"asset_id":"first","price":"0.1","size":"1","side":"BUY"},
                {"asset_id":"second","price":"invalid","size":"1","side":"SELL"}
              ]
            }
            """);

        var result = _normalizer.Normalize(CreateRawEvent(json));

        AssertInvalid(
            result,
            "normalization.field.decimal.invalid",
            "price_changes[1].price");
        result.Event.Should().BeNull();
    }

    [Fact]
    public void Normalize_NonObjectSecondItem_ShouldReturnIndexedItemIssue()
    {
        var json = ParseObject(
            """
            {
              "market": "market",
              "price_changes": [
                {"asset_id":"first","price":"0.1","size":"1","side":"BUY"},
                null
              ]
            }
            """);

        var result = _normalizer.Normalize(CreateRawEvent(json));

        AssertInvalid(
            result,
            "normalization.field.object.invalid",
            "price_changes[1]");
    }

    [Theory]
    [MemberData(nameof(InvalidItemFields))]
    public void Normalize_InvalidItemField_ShouldReturnNestedFieldPath(
        string item,
        string expectedField,
        string expectedCode)
    {
        var json = ParseObject(
            $"{{\"market\":\"market\",\"price_changes\":[{item}]}}");

        var result = _normalizer.Normalize(CreateRawEvent(json));

        AssertInvalid(result, expectedCode, expectedField);
    }

    [Fact]
    public void Normalize_OptionalFieldsAndUnknownFields_ShouldPreserveConfirmedContract()
    {
        var json = ParseObject(
            """
            {
              "market": "market",
              "timestamp": null,
              "unknown_root": {"secret": true},
              "price_changes": [
                {
                  "asset_id": "asset",
                  "price": "0",
                  "size": "0",
                  "side": "SELL",
                  "hash": "",
                  "best_bid": null,
                  "best_ask": null,
                  "unknown_item": [1, 2]
                }
              ]
            }
            """);

        var result = _normalizer.Normalize(CreateRawEvent(json));

        var normalizedEvent = result.Event!;
        var record = (PriceChangeRecord)normalizedEvent.Records.Single();
        result.Outcome.Should().Be(NormalizationOutcome.Processed);
        normalizedEvent.SourceTimestamp.Should().BeNull();
        record.ItemIndex.Should().Be(0);
        record.Hash.Should().BeEmpty();
        record.BestBid.Should().BeNull();
        record.BestAsk.Should().BeNull();
    }

    [Theory]
    [InlineData("price", "-0.01")]
    [InlineData("price", "1.01")]
    [InlineData("best_bid", "-0.01")]
    [InlineData("best_bid", "1.01")]
    [InlineData("best_ask", "-0.01")]
    [InlineData("best_ask", "1.01")]
    public void Normalize_PriceOutsideRange_ShouldReturnInvalid(
        string field,
        string value)
    {
        var optionalField = field == "price"
            ? string.Empty
            : $",\"{field}\":\"{value}\"";
        var price = field == "price" ? value : "0.5";
        var json = ParseObject(
            $"{{\"market\":\"market\",\"price_changes\":[{{\"asset_id\":\"asset\",\"price\":\"{price}\",\"size\":\"1\",\"side\":\"BUY\"{optionalField}}}]}}");

        var result = _normalizer.Normalize(CreateRawEvent(json));

        AssertInvalid(
            result,
            "normalization.field.range.invalid",
            $"price_changes[0].{field}");
    }

    [Fact]
    public void Normalize_InvalidValue_ShouldNotIncludeValueOrPayloadInIssue()
    {
        const string secret = "private-price-value";
        var json = ParseObject(
            $"{{\"market\":\"market\",\"price_changes\":[{{\"asset_id\":\"asset\",\"price\":\"{secret}\",\"size\":\"1\",\"side\":\"BUY\",\"other\":\"do-not-leak\"}}]}}");

        var result = _normalizer.Normalize(CreateRawEvent(json));

        result.Issue!.Message.Should().NotContain(secret);
        result.Issue.Message.Should().NotContain("do-not-leak");
        result.Issue.Message.Should().NotContain("{");
    }

    [Fact]
    public void Contract_ShouldDeclareExpectedEventTypeAndVersion()
    {
        _normalizer.EventType.Should().Be("price_change");
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

    private static LogicalRawEvent CreateRawEvent(JsonElement json)
    {
        return new LogicalRawEvent(
            rawMessageId: 42,
            rawItemIndex: 2,
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
        var assembly = typeof(PriceChangeNormalizerTests).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith($".{fileName}", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }
}
