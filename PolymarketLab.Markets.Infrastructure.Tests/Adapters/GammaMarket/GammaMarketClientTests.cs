using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects;
using PolymarketLab.Markets.Core.Ports.Dto;
using PolymarketLab.Markets.Infrastructure.Adapters.GammaMarket;
using PolymarketLab.SharedKernel.Errors;
using Xunit;

namespace PolymarketLab.Markets.Infrastructure.Tests.Adapters.GammaMarket;

public sealed class GammaMarketClientTests
{
    private static readonly EventSlug EventSlug = EventSlug.Create("rain-event").Value;
    private static readonly MarketSlug Slug = MarketSlug.Create("will-it-rain").Value;

    [Fact]
    public async Task GetByEventSlugAsync_WithSingleMarket_ShouldMapEventAndChildMarket()
    {
        Uri? requestUri = null;
        var client = CreateClient((request, _) =>
        {
            requestUri = request.RequestUri;
            return Task.FromResult(JsonResponse(CreateEventPayload([CreatePayload()])));
        });

        var result = await client.GetByEventSlugAsync(EventSlug, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        requestUri.Should().Be("https://gamma-api.polymarket.com/events/slug/rain-event");
        result.Value.ExternalEventId.Should().Be("event-123");
        result.Value.Slug.Should().Be("rain-event");
        result.Value.Market.Slug.Should().Be("will-it-rain");
        result.Value.Market.Tokens.Should().Equal(
            new ExternalMarketToken("Yes", "token-yes", 0),
            new ExternalMarketToken("No", "token-no", 1));
    }

    [Fact]
    public async Task GetByEventSlugAsync_WithoutMarkets_ShouldReturnMarketCountError()
    {
        var client = CreateClient((_, _) =>
            Task.FromResult(JsonResponse(CreateEventPayload([]))));

        var result = await client.GetByEventSlugAsync(EventSlug, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gamma.event.market_count_invalid");
        result.Error.InvalidField.Should().Be("markets");
    }

    [Fact]
    public async Task GetByEventSlugAsync_WithMultipleMarkets_ShouldReturnMarketCountError()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(
            CreateEventPayload([CreatePayload(), CreatePayload()]))));

        var result = await client.GetByEventSlugAsync(EventSlug, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gamma.event.market_count_invalid");
        result.Error.Message.Should().Contain("contained 2");
    }

    [Fact]
    public async Task GetByEventSlugAsync_WithoutMarketsField_ShouldReturnRequiredFieldError()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(
            JsonSerializer.Serialize(new { id = "event-123", slug = "rain-event" }))));

        var result = await client.GetByEventSlugAsync(EventSlug, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gamma.event.field.required");
        result.Error.InvalidField.Should().Be("markets");
    }

    [Fact]
    public async Task GetByEventSlugAsync_WithInvalidChildMarket_ShouldPreserveMarketMappingError()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(
            CreateEventPayload([CreatePayload(includeQuestion: false)]))));

        var result = await client.GetByEventSlugAsync(EventSlug, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gamma.market.field.required");
        result.Error.InvalidField.Should().Be("question");
    }

    [Fact]
    public async Task GetByEventSlugAsync_WithNullChildMarket_ShouldReturnInvalidJsonError()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(
            """
            {"id":"event-123","slug":"rain-event","markets":[null]}
            """)));

        var result = await client.GetByEventSlugAsync(EventSlug, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gamma.event.invalid_json");
    }

    [Fact]
    public async Task GetByEventSlugAsync_WhenResponseBodyReadFails_ShouldReturnEventNetworkError()
    {
        var client = CreateClient((_, _) => Task.FromResult(
            StreamResponse(new FailingReadStream())));

        var result = await client.GetByEventSlugAsync(EventSlug, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gamma.event.network");
    }

    [Fact]
    public async Task GetByEventSlugAsync_WithNotFoundResponse_ShouldReturnEventNotFoundError()
    {
        var client = CreateClient((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await client.GetByEventSlugAsync(EventSlug, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gamma.event.not_found");
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetByEventSlugAsync_WhenRequestTimesOut_ShouldReturnEventTimeoutError()
    {
        var client = CreateClient((_, _) => throw new TaskCanceledException());

        var result = await client.GetByEventSlugAsync(EventSlug, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gamma.event.timeout");
    }

    [Fact]
    public async Task GetByEventSlugAsync_WhenCallerCancels_ShouldPropagateCancellation()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        var client = CreateClient((_, token) => Task.FromCanceled<HttpResponseMessage>(token));

        var action = () => client.GetByEventSlugAsync(EventSlug, cancellationTokenSource.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetByEventSlugAsync_WithNetworkError_ShouldReturnEventNetworkError()
    {
        var client = CreateClient((_, _) => throw new HttpRequestException("DNS failure"));

        var result = await client.GetByEventSlugAsync(EventSlug, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gamma.event.network");
    }

    [Fact]
    public async Task GetByEventSlugAsync_WithUnexpectedHttpStatus_ShouldReturnEventHttpError()
    {
        var client = CreateClient((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        var result = await client.GetByEventSlugAsync(EventSlug, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gamma.event.http_error");
        result.Error.Message.Should().Contain("503");
    }

    [Fact]
    public async Task GetByEventSlugAsync_WithInvalidJson_ShouldReturnEventInvalidJsonError()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse("{invalid")));

        var result = await client.GetByEventSlugAsync(EventSlug, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gamma.event.invalid_json");
    }

    [Fact]
    public async Task GetByMarketSlugAsync_WithValidResponse_ShouldMapExternalMarket()
    {
        HttpMethod? requestMethod = null;
        Uri? requestUri = null;
        var client = CreateClient((request, _) =>
        {
            requestMethod = request.Method;
            requestUri = request.RequestUri;
            return Task.FromResult(JsonResponse(CreatePayload()));
        });

        var result = await client.GetByMarketSlugAsync(Slug, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        requestMethod.Should().Be(HttpMethod.Get);
        requestUri.Should().Be("https://gamma-api.polymarket.com/markets/slug/will-it-rain");
        result.Value.Should().BeEquivalentTo(new ExternalMarket(
            "market-123",
            "will-it-rain",
            "Will it rain?",
            "0xcondition",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
            true,
            false,
            true,
            true,
            [
                new ExternalMarketToken("Yes", "token-yes", 0),
                new ExternalMarketToken("No", "token-no", 1)
            ]), options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task GetByMarketSlugAsync_WithNotFoundResponse_ShouldReturnNotFoundError()
    {
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await client.GetByMarketSlugAsync(Slug, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gamma.market.not_found");
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetByMarketSlugAsync_WhenRequestTimesOut_ShouldReturnTimeoutError()
    {
        var client = CreateClient((_, _) => throw new TaskCanceledException());

        var result = await client.GetByMarketSlugAsync(Slug, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gamma.market.timeout");
    }

    [Fact]
    public async Task GetByMarketSlugAsync_WhenCallerCancels_ShouldPropagateCancellation()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        var client = CreateClient((_, token) => Task.FromCanceled<HttpResponseMessage>(token));

        var action = () => client.GetByMarketSlugAsync(Slug, cancellationTokenSource.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetByMarketSlugAsync_WithNetworkError_ShouldReturnNetworkError()
    {
        var client = CreateClient((_, _) => throw new HttpRequestException("DNS failure"));

        var result = await client.GetByMarketSlugAsync(Slug, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gamma.market.network");
    }

    [Fact]
    public async Task GetByMarketSlugAsync_WithUnexpectedHttpStatus_ShouldReturnHttpError()
    {
        var client = CreateClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        var result = await client.GetByMarketSlugAsync(Slug, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gamma.market.http_error");
        result.Error.Message.Should().Contain("503");
    }

    [Fact]
    public async Task GetByMarketSlugAsync_WithInvalidJson_ShouldReturnInvalidJsonError()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse("{invalid")));

        var result = await client.GetByMarketSlugAsync(Slug, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gamma.market.invalid_json");
    }

    [Fact]
    public async Task GetByMarketSlugAsync_WithMissingRequiredField_ShouldReturnRequiredFieldError()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(CreatePayload(includeQuestion: false))));

        var result = await client.GetByMarketSlugAsync(Slug, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gamma.market.field.required");
        result.Error.InvalidField.Should().Be("question");
    }

    [Fact]
    public async Task GetByMarketSlugAsync_WithInvalidEmbeddedJson_ShouldReturnInvalidJsonError()
    {
        var client = CreateClient((_, _) =>
            Task.FromResult(JsonResponse(CreatePayload(outcomes: "not-json"))));

        var result = await client.GetByMarketSlugAsync(Slug, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gamma.market.invalid_json");
    }

    [Fact]
    public async Task GetByMarketSlugAsync_WithDifferentArrayLengths_ShouldReturnMismatchError()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(CreatePayload(
            tokenIds: "[\"token-yes\"]"))));

        var result = await client.GetByMarketSlugAsync(Slug, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gamma.market.token_count_mismatch");
        result.Error.Type.Should().Be(ErrorType.InvalidSize);
    }

    [Fact]
    public async Task GetByMarketSlugAsync_WithEmptyTokenId_ShouldReturnEmptyTokenError()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(CreatePayload(
            tokenIds: "[\"token-yes\", \" \" ]"))));

        var result = await client.GetByMarketSlugAsync(Slug, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gamma.market.token_id.empty");
        result.Error.InvalidField.Should().Be("clobTokenIds[1]");
    }

    [Fact]
    public async Task GetByMarketSlugAsync_WithDisabledOrderBook_ShouldMapExternalState()
    {
        var client = CreateClient((_, _) =>
            Task.FromResult(JsonResponse(CreatePayload(enableOrderBook: false))));

        var result = await client.GetByMarketSlugAsync(Slug, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.OrderBookEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task GetByMarketSlugAsync_WithoutAcceptingOrders_ShouldReturnRequiredFieldError()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(
            CreatePayload(includeAcceptingOrders: false))));

        var result = await client.GetByMarketSlugAsync(Slug, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gamma.market.field.required");
        result.Error.InvalidField.Should().Be("acceptingOrders");
    }

    private static GammaMarketClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
    {
        return new GammaMarketClient(new HttpClient(new StubHttpMessageHandler(sendAsync)));
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage StreamResponse(Stream stream)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream)
        };
    }

    private static string CreatePayload(
        bool enableOrderBook = true,
        string outcomes = "[\"Yes\", \"No\"]",
        string tokenIds = "[\"token-yes\", \"token-no\"]",
        bool includeQuestion = true,
        bool includeAcceptingOrders = true)
    {
        var payload = new Dictionary<string, object?>
        {
            ["id"] = "market-123",
            ["slug"] = "will-it-rain",
            ["conditionId"] = "0xcondition",
            ["startDate"] = "2026-01-01T00:00:00Z",
            ["endDate"] = "2026-02-01T00:00:00Z",
            ["active"] = true,
            ["closed"] = false,
            ["enableOrderBook"] = enableOrderBook,
            ["outcomes"] = outcomes,
            ["clobTokenIds"] = tokenIds
        };

        if (includeQuestion)
            payload["question"] = "Will it rain?";

        if (includeAcceptingOrders)
            payload["acceptingOrders"] = true;

        return JsonSerializer.Serialize(payload);
    }

    private static string CreateEventPayload(IEnumerable<string> markets)
    {
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = "event-123",
            ["slug"] = "rain-event",
            ["markets"] = markets
                .Select(market => JsonDocument.Parse(market).RootElement.Clone())
                .ToArray()
        });
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return sendAsync(request, cancellationToken);
        }
    }

    private sealed class FailingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new IOException("Connection interrupted.");

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("Connection interrupted."));

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
