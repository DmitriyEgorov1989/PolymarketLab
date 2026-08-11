using System.Text.Json;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Normalization;

public sealed class PolymarketJsonReaderTests
{
    public static TheoryData<string, decimal> ValidDecimals => new()
    {
        { "0", 0m },
        { "1", 1m },
        { "0.001", 0.001m },
        { "3400.87", 3400.87m },
        { "-1", -1m },
        { "+1", 1m },
        { ".5", 0.5m },
        { "1.", 1m }
    };

    [Fact]
    public void ReadRequiredString_StringValue_ShouldPreserveValueWithoutTrimming()
    {
        var json = ParseObject("""{"market":"  рынок  ","unknown":{"secret":true}}""");

        var result = PolymarketJsonReader.ReadRequiredString(json, "market");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("  рынок  ");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"market\":null}")]
    [InlineData("{\"market\":\"\"}")]
    [InlineData("{\"market\":\"   \"}")]
    public void ReadRequiredString_MissingNullOrEmpty_ShouldReturnRequiredIssue(string json)
    {
        var result = PolymarketJsonReader.ReadRequiredString(ParseObject(json), "market");

        AssertIssue(result.Error, "normalization.field.required", "market");
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void ReadRequiredString_NonStringValue_ShouldReturnStringIssue(string value)
    {
        var result = PolymarketJsonReader.ReadRequiredString(
            ParseObject($"{{\"market\":{value}}}"),
            "market");

        AssertIssue(result.Error, "normalization.field.string.invalid", "market");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"message\":null}")]
    public void ReadOptionalString_MissingOrNull_ShouldReturnSuccessfulNull(string json)
    {
        var result = PolymarketJsonReader.ReadOptionalString(ParseObject(json), "message");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" value ")]
    public void ReadOptionalString_StringValue_ShouldPreserveValue(string value)
    {
        var json = ParseObject(JsonSerializer.Serialize(new { message = value }));

        var result = PolymarketJsonReader.ReadOptionalString(json, "message");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(value);
    }

    [Fact]
    public void ReadOptionalString_NonStringValue_ShouldReturnStringIssue()
    {
        var result = PolymarketJsonReader.ReadOptionalString(
            ParseObject("""{"message":false}"""),
            "message");

        AssertIssue(result.Error, "normalization.field.string.invalid", "message");
    }

    [Theory]
    [MemberData(nameof(ValidDecimals))]
    public void ReadRequiredDecimal_InvariantString_ShouldReturnDecimal(
        string value,
        decimal expected)
    {
        var result = PolymarketJsonReader.ReadRequiredDecimal(
            ParseObject($"{{\"price\":\"{value}\"}}"),
            "price");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("1,5")]
    [InlineData("1,000")]
    [InlineData("1e3")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("79228162514264337593543950336")]
    public void ReadRequiredDecimal_InvalidString_ShouldReturnDecimalIssue(string value)
    {
        var result = PolymarketJsonReader.ReadRequiredDecimal(
            ParseObject($"{{\"price\":\"{value}\"}}"),
            "price");

        AssertIssue(result.Error, "normalization.field.decimal.invalid", "price");
        result.Error.Message.Should().NotContain(value);
    }

    [Fact]
    public void ReadRequiredDecimal_JsonNumber_ShouldReturnStringIssueWithoutUsingFloatingPoint()
    {
        var result = PolymarketJsonReader.ReadRequiredDecimal(
            ParseObject("""{"price":0.3}"""),
            "price");

        AssertIssue(result.Error, "normalization.field.string.invalid", "price");
    }

    [Fact]
    public void ReadRequiredDecimal_EmptyString_ShouldNotBecomeZero()
    {
        var result = PolymarketJsonReader.ReadRequiredDecimal(
            ParseObject("""{"price":""}"""),
            "price");

        AssertIssue(result.Error, "normalization.field.required", "price");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"size\":null}")]
    public void ReadOptionalDecimal_MissingOrNull_ShouldReturnSuccessfulNull(string json)
    {
        var result = PolymarketJsonReader.ReadOptionalDecimal(ParseObject(json), "size");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public void ReadOptionalDecimal_ValidString_ShouldReturnDecimal()
    {
        var result = PolymarketJsonReader.ReadOptionalDecimal(
            ParseObject("""{"size":"6.25"}"""),
            "size");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(6.25m);
    }

    [Fact]
    public void ReadOptionalDecimal_EmptyString_ShouldReturnDecimalIssue()
    {
        var result = PolymarketJsonReader.ReadOptionalDecimal(
            ParseObject("""{"size":""}"""),
            "size");

        AssertIssue(result.Error, "normalization.field.decimal.invalid", "size");
    }

    [Theory]
    [InlineData("1785490103413", 1785490103413L)]
    [InlineData("0", 0L)]
    [InlineData("-1", -1L)]
    [InlineData("+1", 1L)]
    public void ReadRequiredEpochMilliseconds_IntegerString_ShouldReturnLong(
        string value,
        long expected)
    {
        var result = PolymarketJsonReader.ReadRequiredEpochMilliseconds(
            ParseObject($"{{\"timestamp\":\"{value}\"}}"),
            "timestamp");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("1e3")]
    [InlineData("2026-08-10T10:00:00Z")]
    [InlineData("9223372036854775808")]
    public void ReadRequiredEpochMilliseconds_InvalidString_ShouldReturnTimestampIssue(
        string value)
    {
        var result = PolymarketJsonReader.ReadRequiredEpochMilliseconds(
            ParseObject($"{{\"timestamp\":\"{value}\"}}"),
            "timestamp");

        AssertIssue(result.Error, "normalization.field.timestamp.invalid", "timestamp");
        result.Error.Message.Should().NotContain(value);
    }

    [Fact]
    public void ReadRequiredEpochMilliseconds_JsonNumber_ShouldReturnStringIssue()
    {
        var result = PolymarketJsonReader.ReadRequiredEpochMilliseconds(
            ParseObject("""{"timestamp":1785490103413}"""),
            "timestamp");

        AssertIssue(result.Error, "normalization.field.string.invalid", "timestamp");
    }

    [Fact]
    public void ReadRequiredEpochMilliseconds_EmptyString_ShouldNotBecomeZero()
    {
        var result = PolymarketJsonReader.ReadRequiredEpochMilliseconds(
            ParseObject("""{"timestamp":""}"""),
            "timestamp");

        AssertIssue(result.Error, "normalization.field.required", "timestamp");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"timestamp\":null}")]
    public void ReadOptionalEpochMilliseconds_MissingOrNull_ShouldReturnSuccessfulNull(
        string json)
    {
        var result = PolymarketJsonReader.ReadOptionalEpochMilliseconds(
            ParseObject(json),
            "timestamp");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public void ReadOptionalEpochMilliseconds_EmptyString_ShouldReturnTimestampIssue()
    {
        var result = PolymarketJsonReader.ReadOptionalEpochMilliseconds(
            ParseObject("""{"timestamp":""}"""),
            "timestamp");

        AssertIssue(result.Error, "normalization.field.timestamp.invalid", "timestamp");
    }

    [Theory]
    [InlineData("BUY", TradeSide.Buy)]
    [InlineData("SELL", TradeSide.Sell)]
    public void ReadRequiredTradeSide_KnownValue_ShouldReturnTradeSide(
        string value,
        TradeSide expected)
    {
        var result = PolymarketJsonReader.ReadRequiredTradeSide(
            ParseObject($"{{\"side\":\"{value}\"}}"),
            "side");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("buy")]
    [InlineData("Sell")]
    [InlineData(" BUY ")]
    [InlineData("BID")]
    public void ReadRequiredTradeSide_UnknownValue_ShouldReturnTradeSideIssue(string value)
    {
        var result = PolymarketJsonReader.ReadRequiredTradeSide(
            ParseObject($"{{\"side\":\"{value}\"}}"),
            "side");

        AssertIssue(result.Error, "normalization.field.trade_side.invalid", "side");
        result.Error.Message.Should().NotContain(value);
    }

    [Fact]
    public void InvalidValue_IssueShouldNotContainFieldValueOrFullJson()
    {
        const string secret = "private-payload-value";
        var json = ParseObject($"{{\"price\":\"{secret}\",\"other\":\"do-not-leak\"}}");

        var result = PolymarketJsonReader.ReadRequiredDecimal(json, "price");

        result.Error.Field.Should().Be("price");
        result.Error.Message.Should().NotContain(secret);
        result.Error.Message.Should().NotContain("do-not-leak");
        result.Error.Message.Should().NotContain("{");
    }

    [Fact]
    public void LastTradePriceFixture_ShouldUseObservedStringContract()
    {
        var json = ReadFixture("last-trade-price.json");

        PolymarketJsonReader.ReadRequiredString(json, "market").IsSuccess.Should().BeTrue();
        PolymarketJsonReader.ReadRequiredDecimal(json, "price").Value.Should().Be(0.3m);
        PolymarketJsonReader.ReadRequiredDecimal(json, "size").Value.Should().Be(6m);
        PolymarketJsonReader.ReadRequiredEpochMilliseconds(json, "timestamp").Value
            .Should().Be(1785490103413L);
        PolymarketJsonReader.ReadRequiredTradeSide(json, "side").Value
            .Should().Be(TradeSide.Buy);
    }

    [Fact]
    public void PriceChangeFixture_ShouldParseBothObservedTradeSides()
    {
        var json = ReadFixture("price-change.json");
        var changes = json.GetProperty("price_changes").EnumerateArray().ToArray();

        PolymarketJsonReader.ReadRequiredDecimal(changes[0], "price").Value.Should().Be(0.1m);
        PolymarketJsonReader.ReadRequiredTradeSide(changes[0], "side").Value
            .Should().Be(TradeSide.Buy);
        PolymarketJsonReader.ReadRequiredDecimal(changes[1], "price").Value.Should().Be(0.9m);
        PolymarketJsonReader.ReadRequiredTradeSide(changes[1], "side").Value
            .Should().Be(TradeSide.Sell);
    }

    private static JsonElement ParseObject(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonElement ReadFixture(string fileName)
    {
        var assembly = typeof(PolymarketJsonReaderTests).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith($".{fileName}", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    private static void AssertIssue(
        NormalizationIssue issue,
        string expectedCode,
        string expectedField)
    {
        issue.Code.Should().Be(expectedCode);
        issue.Message.Should().NotBeNullOrWhiteSpace();
        issue.Field.Should().Be(expectedField);
    }
}
