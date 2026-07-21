using FluentAssertions;
using PolymarketLab.Markets.Core.Application.Extensions;
using PolymarketLab.SharedKernel.Errors;
using Xunit;

namespace PolymarketLab.Markets.Domain.Tests.Application.Services;

public class PolymarketUrlExtensionsTests
{
    [Theory]
    [InlineData("https://polymarket.com/event/will-it-rain", "will-it-rain")]
    [InlineData("https://polymarket.com/ru/event/will-it-rain", "will-it-rain")]
    [InlineData("https://polymarket.com/event/will-it-rain?source=test", "will-it-rain")]
    [InlineData("https://polymarket.com/event/will-it-rain/", "will-it-rain")]
    public void Parse_WithValidUrl_ShouldReturnSlug(string url, string expectedSlug)
    {
        var result = url.ParsePolymarketSlug();

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(expectedSlug);
    }

    [Theory]
    [InlineData(null, "polymarket.url.empty", "URL is empty.", ErrorType.ValueIsRequired)]
    [InlineData("", "polymarket.url.empty", "URL is empty.", ErrorType.ValueIsRequired)]
    [InlineData(" ", "polymarket.url.empty", "URL is empty.", ErrorType.ValueIsRequired)]
    [InlineData("not-a-url", "polymarket.url.invalid", "URL is invalid.", ErrorType.ValueIsInvalid)]
    [InlineData("http://polymarket.com/event/will-it-rain", "polymarket.url.https.required", "Only HTTPS URLs are supported.", ErrorType.ValueIsInvalid)]
    [InlineData("https://example.com/event/will-it-rain", "polymarket.url.host.invalid", "URL must belong to polymarket.com.", ErrorType.ValueIsInvalid)]
    [InlineData("https://polymarket.com/markets/will-it-rain", "polymarket.url.event.missing", "URL does not contain an event segment.", ErrorType.ValueIsInvalid)]
    [InlineData("https://polymarket.com/event/", "polymarket.url.slug.missing", "Market slug is missing.", ErrorType.ValueIsRequired)]
    [InlineData("https://polymarket.com/event//other", "polymarket.url.slug.missing", "Market slug is missing.", ErrorType.ValueIsRequired)]
    public void Parse_WithInvalidUrl_ShouldReturnSpecificError(
        string? url,
        string expectedCode,
        string expectedMessage,
        ErrorType expectedType)
    {
        var result = url.ParsePolymarketSlug();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(expectedCode);
        result.Error.Message.Should().Be(expectedMessage);
        result.Error.Type.Should().Be(expectedType);
    }
}
