using System.Text.Json;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.Normalization;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.Normalization;

public sealed class NormalizationDispatcherTests
{
    public static TheoryData<string, string> InvalidEventTypes => new()
    {
        { "{}", "normalization.event_type.required" },
        { "{\"event_type\":null}", "normalization.event_type.required" },
        { "{\"event_type\":\"\"}", "normalization.event_type.required" },
        { "{\"event_type\":\"   \"}", "normalization.event_type.required" },
        { "{\"event_type\":1}", "normalization.event_type.invalid" },
        { "{\"event_type\":true}", "normalization.event_type.invalid" },
        { "{\"event_type\":{}}", "normalization.event_type.invalid" },
        { "{\"event_type\":[]}", "normalization.event_type.invalid" }
    };

    [Fact]
    public void Dispatch_KnownEventType_ShouldInvokeOnlyMatchingNormalizerAndReturnItsResult()
    {
        var rawEvent = CreateRawEvent("""{"event_type":"book","extra":true}""");
        var expected = NormalizationResult.Invalid(
            rawEvent.RawItemIndex,
            2,
            new NormalizationIssue("normalization.book.invalid", "Book is invalid."));
        var bookNormalizer = new StubNormalizer("book", 2, expected);
        var priceNormalizer = new StubNormalizer(
            "price_change",
            1,
            NormalizationResult.Unsupported(
                rawEvent.RawItemIndex,
                new NormalizationIssue("unused", "Unused result.")));
        var dispatcher = new NormalizationDispatcher([bookNormalizer, priceNormalizer]);

        var result = dispatcher.Dispatch(rawEvent);

        result.Should().BeSameAs(expected);
        bookNormalizer.Calls.Should().Be(1);
        bookNormalizer.LastRawEvent.Should().BeSameAs(rawEvent);
        priceNormalizer.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("BOOK")]
    [InlineData(" book ")]
    public void Dispatch_UnknownOrCaseMismatchedEventType_ShouldReturnUnsupported(
        string eventType)
    {
        var normalizer = CreateUnusedNormalizer("book");
        var dispatcher = new NormalizationDispatcher([normalizer]);

        var result = dispatcher.Dispatch(CreateRawEvent($"{{\"event_type\":\"{eventType}\"}}"));

        result.Outcome.Should().Be(NormalizationOutcome.Unsupported);
        result.NormalizerVersion.Should().BeNull();
        result.Event.Should().BeNull();
        result.Issue.Should().BeEquivalentTo(new NormalizationIssue(
            "normalization.event_type.unsupported",
            "Event type is not supported.",
            "event_type"));
        normalizer.Calls.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(InvalidEventTypes))]
    public void Dispatch_InvalidEventType_ShouldReturnInvalidWithoutNormalizerVersion(
        string json,
        string expectedIssueCode)
    {
        var normalizer = CreateUnusedNormalizer("book");
        var dispatcher = new NormalizationDispatcher([normalizer]);

        var result = dispatcher.Dispatch(CreateRawEvent(json, rawItemIndex: 3));

        result.RawItemIndex.Should().Be(3);
        result.Outcome.Should().Be(NormalizationOutcome.Invalid);
        result.NormalizerVersion.Should().BeNull();
        result.Event.Should().BeNull();
        result.Issue!.Code.Should().Be(expectedIssueCode);
        result.Issue.Field.Should().Be("event_type");
        normalizer.Calls.Should().Be(0);
    }

    [Fact]
    public void Constructor_DuplicateEventType_ShouldFailBeforeDispatch()
    {
        var first = CreateUnusedNormalizer("book");
        var second = CreateUnusedNormalizer("book");

        var action = () => new NormalizationDispatcher([first, second]);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Event type 'book'*");
    }

    [Fact]
    public void Constructor_EventTypesWithDifferentCase_ShouldRegisterSeparately()
    {
        var lowerResult = NormalizationResult.Unsupported(
            0,
            new NormalizationIssue("lower", "Lower result."));
        var upperResult = NormalizationResult.Unsupported(
            0,
            new NormalizationIssue("upper", "Upper result."));
        var lower = new StubNormalizer("book", 1, lowerResult);
        var upper = new StubNormalizer("BOOK", 1, upperResult);
        var dispatcher = new NormalizationDispatcher([lower, upper]);

        var firstResult = dispatcher.Dispatch(CreateRawEvent("""{"event_type":"book"}"""));
        var secondResult = dispatcher.Dispatch(CreateRawEvent("""{"event_type":"BOOK"}"""));

        firstResult.Should().BeSameAs(lowerResult);
        secondResult.Should().BeSameAs(upperResult);
        lower.Calls.Should().Be(1);
        upper.Calls.Should().Be(1);
    }

    [Fact]
    public void Constructor_EmptyNormalizerCollection_ShouldAllowUnsupportedResult()
    {
        var dispatcher = new NormalizationDispatcher([]);

        var result = dispatcher.Dispatch(CreateRawEvent("""{"event_type":"book"}"""));

        result.Outcome.Should().Be(NormalizationOutcome.Unsupported);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_EmptyNormalizerEventType_ShouldRejectConfiguration(string eventType)
    {
        var normalizer = CreateUnusedNormalizer(eventType);

        var action = () => new NormalizationDispatcher([normalizer]);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*must declare an event type*");
    }

    [Fact]
    public void Constructor_NonPositiveNormalizerVersion_ShouldRejectConfiguration()
    {
        var normalizer = new StubNormalizer(
            "book",
            0,
            NormalizationResult.Unsupported(
                0,
                new NormalizationIssue("unused", "Unused result.")));

        var action = () => new NormalizationDispatcher([normalizer]);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*must declare a positive version*");
    }

    private static StubNormalizer CreateUnusedNormalizer(string eventType)
    {
        return new StubNormalizer(
            eventType,
            1,
            NormalizationResult.Unsupported(
                0,
                new NormalizationIssue("unused", "Unused result.")));
    }

    private static LogicalRawEvent CreateRawEvent(string json, int rawItemIndex = 0)
    {
        using var document = JsonDocument.Parse(json);
        return new LogicalRawEvent(
            rawMessageId: 42,
            rawItemIndex: rawItemIndex,
            projectionVersion: 1,
            sessionId: CollectorSessionId.Create(
                Guid.Parse("6d9ac447-7bcc-4c85-8619-0384da429a33")).Value,
            receivedAt: DateTimeOffset.Parse("2026-08-10T10:00:00Z"),
            json: document.RootElement);
    }

    private sealed class StubNormalizer(
        string eventType,
        int version,
        NormalizationResult result) : IRawMessageNormalizer
    {
        public string EventType { get; } = eventType;
        public int Version { get; } = version;
        public int Calls { get; private set; }
        public LogicalRawEvent? LastRawEvent { get; private set; }

        public NormalizationResult Normalize(LogicalRawEvent rawEvent)
        {
            Calls++;
            LastRawEvent = rawEvent;
            return result;
        }
    }
}
