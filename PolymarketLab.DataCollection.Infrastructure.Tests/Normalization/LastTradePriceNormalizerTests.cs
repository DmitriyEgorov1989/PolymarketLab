using System.Text.Json;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Normalization;

public sealed class LastTradePriceNormalizerTests
{
    private const string TransactionHash =
        "0xc4f3693b273a89bee908ddcc79266b382c985d1e2a4e8ee43687c8c7c964c564";

    private readonly LastTradePriceNormalizer _normalizer = new();

    public static TheoryData<string, string, string> InvalidEvents => new()
    {
        {
            """{"asset_id":"asset","price":"0.5","side":"BUY"}""",
            "market",
            "normalization.field.required"
        },
        {
            """{"market":"market","asset_id":"","price":"0.5","side":"BUY"}""",
            "asset_id",
            "normalization.field.required"
        },
        {
            """{"market":"market","asset_id":"asset","price":"-0.01","side":"BUY"}""",
            "price",
            "normalization.field.range.invalid"
        },
        {
            """{"market":"market","asset_id":"asset","price":"1.01","side":"BUY"}""",
            "price",
            "normalization.field.range.invalid"
        },
        {
            """{"market":"market","asset_id":"asset","price":0.5,"side":"BUY"}""",
            "price",
            "normalization.field.string.invalid"
        },
        {
            """{"market":"market","asset_id":"asset","price":"0.5","size":"-1","side":"BUY"}""",
            "size",
            "normalization.field.range.invalid"
        },
        {
            """{"market":"market","asset_id":"asset","price":"0.5","side":"HOLD"}""",
            "side",
            "normalization.field.trade_side.invalid"
        },
        {
            """{"market":"market","asset_id":"asset","price":"0.5","side":"BUY","timestamp":"now"}""",
            "timestamp",
            "normalization.field.timestamp.invalid"
        },
        {
            """{"market":"market","asset_id":"asset","price":"0.5","side":"BUY","fee_rate_bps":"free"}""",
            "fee_rate_bps",
            "normalization.field.decimal.invalid"
        },
        {
            """{"market":"market","asset_id":"asset","price":"0.5","side":"BUY","transaction_hash":1}""",
            "transaction_hash",
            "normalization.field.string.invalid"
        }
    };

    [Fact]
    public void Normalize_RealFixture_ShouldCreateSingleLastTradeRecordAndHeader()
    {
        var rawEvent = CreateRawEvent(ReadFixture("last-trade-price.json"));

        var result = _normalizer.Normalize(rawEvent);

        result.Outcome.Should().Be(NormalizationOutcome.Processed);
        result.NormalizerVersion.Should().Be(1);
        result.Issue.Should().BeNull();
        result.Event.Should().NotBeNull();
        var normalizedEvent = result.Event!;
        normalizedEvent.RawMessageId.Should().Be(42);
        normalizedEvent.RawItemIndex.Should().Be(2);
        normalizedEvent.ProjectionVersion.Should().Be(3);
        normalizedEvent.NormalizerVersion.Should().Be(1);
        normalizedEvent.EventType.Should().Be("last_trade_price");
        normalizedEvent.SessionId.Should().Be(CreateSessionId());
        normalizedEvent.ReceivedAt.Should().Be(DateTimeOffset.Parse("2026-08-10T10:00:00Z"));
        normalizedEvent.SourceTimestamp.Should().Be(1785490103413L);
        normalizedEvent.MarketConditionId.Should().Be(
            "0x69680df36dd7a982c9b18ebc0fda048ae1cf543510abe8446ab55e5403dd923e");
        normalizedEvent.AssetId.Should().Be(
            "9852683497230148976233778745433163015590012473866950809045917104353935531110");

        var record = normalizedEvent.Records.Should().ContainSingle().Which
            .Should().BeOfType<LastTradeRecord>().Subject;
        record.Price.Should().Be(0.3m);
        record.Size.Should().Be(6m);
        record.Side.Should().Be(TradeSide.Buy);
        record.FeeRateBps.Should().Be(0m);
        record.TransactionHash.Should().Be(TransactionHash);
    }

    [Theory]
    [InlineData(
        """{"market":"market","asset_id":"asset","price":"0","size":"0","side":"BUY"}""",
        "0",
        TradeSide.Buy)]
    [InlineData(
        """{"market":"market","asset_id":"asset","price":"1","size":"0","side":"SELL"}""",
        "1",
        TradeSide.Sell)]
    public void Normalize_BoundaryValues_ShouldProcess(
        string json,
        string expectedPrice,
        TradeSide expectedSide)
    {
        var result = _normalizer.Normalize(CreateRawEvent(ParseObject(json)));

        var record = (LastTradeRecord)result.Event!.Records.Single();
        result.Outcome.Should().Be(NormalizationOutcome.Processed);
        record.Price.Should().Be(decimal.Parse(
            expectedPrice,
            System.Globalization.CultureInfo.InvariantCulture));
        record.Size.Should().Be(0m);
        record.Side.Should().Be(expectedSide);
    }

    [Theory]
    [InlineData(
        """{"market":"market","asset_id":"asset","price":"0.5","side":"SELL","extra_scalar":1,"extra_object":{"nested":true},"extra_array":[1,2]}""")]
    [InlineData(
        """{"market":"market","asset_id":"asset","price":"0.5","size":null,"side":"SELL","timestamp":null,"fee_rate_bps":null,"transaction_hash":null}""")]
    public void Normalize_MissingOrNullOptionalFieldsAndExtraFields_ShouldProcess(string json)
    {
        var result = _normalizer.Normalize(CreateRawEvent(ParseObject(json)));

        var normalizedEvent = result.Event!;
        var record = (LastTradeRecord)normalizedEvent.Records.Single();
        result.Outcome.Should().Be(NormalizationOutcome.Processed);
        normalizedEvent.SourceTimestamp.Should().BeNull();
        record.Size.Should().BeNull();
        record.FeeRateBps.Should().BeNull();
        record.TransactionHash.Should().BeNull();
    }

    [Fact]
    public void Normalize_EmptyTransactionHash_ShouldPreserveExternalValue()
    {
        var rawEvent = CreateRawEvent(ParseObject(
            """{"market":"market","asset_id":"asset","price":"0.5","side":"BUY","transaction_hash":""}"""));

        var result = _normalizer.Normalize(rawEvent);

        var record = (LastTradeRecord)result.Event!.Records.Single();
        record.TransactionHash.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(InvalidEvents))]
    public void Normalize_InvalidSupportedEvent_ShouldReturnInvalidWithVersionAndField(
        string json,
        string expectedField,
        string expectedCode)
    {
        var result = _normalizer.Normalize(CreateRawEvent(ParseObject(json)));

        result.RawItemIndex.Should().Be(2);
        result.Outcome.Should().Be(NormalizationOutcome.Invalid);
        result.NormalizerVersion.Should().Be(1);
        result.Event.Should().BeNull();
        result.Issue.Should().NotBeNull();
        result.Issue!.Code.Should().Be(expectedCode);
        result.Issue.Field.Should().Be(expectedField);
    }

    [Fact]
    public void Normalize_InvalidValue_ShouldNotIncludeValueOrPayloadInIssue()
    {
        const string secret = "private-price-value";
        var json = ParseObject(
            $"{{\"market\":\"market\",\"asset_id\":\"asset\",\"price\":\"{secret}\",\"side\":\"BUY\",\"other\":\"do-not-leak\"}}");

        var result = _normalizer.Normalize(CreateRawEvent(json));

        result.Issue!.Message.Should().NotContain(secret);
        result.Issue.Message.Should().NotContain("do-not-leak");
        result.Issue.Message.Should().NotContain("{");
    }

    [Fact]
    public void Contract_ShouldDeclareExpectedEventTypeAndVersion()
    {
        _normalizer.EventType.Should().Be("last_trade_price");
        _normalizer.Version.Should().Be(1);
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
        var assembly = typeof(LastTradePriceNormalizerTests).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith($".{fileName}", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }
}
