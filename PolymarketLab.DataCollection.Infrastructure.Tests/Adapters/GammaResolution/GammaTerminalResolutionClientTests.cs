using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.GammaResolution;
using PolymarketLab.SharedKernel.Errors;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.GammaResolution;

public sealed class GammaTerminalResolutionClientTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 8, 27, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_WithTerminalResponse_ShouldReturnSingleWinner()
    {
        HttpMethod? requestMethod = null;
        Uri? requestUri = null;
        var client = CreateClient((request, _) =>
        {
            requestMethod = request.Method;
            requestUri = request.RequestUri;
            return Task.FromResult(JsonResponse(CreatePayload()));
        });

        var result = await client.GetAsync(CreateRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        requestMethod.Should().Be(HttpMethod.Get);
        requestUri!.AbsoluteUri.Should().Be(
            "https://gamma-api.polymarket.com/events/slug/event%2Fwith%20space");
        result.Value.Status.Should().Be(GammaTerminalResolutionStatus.Terminal);
        result.Value.ObservedAt.Should().Be(ObservedAt);
        result.Value.ExternalClosedAt.Should().Be(
            new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero));
        result.Value.Outcomes.Should().Equal(
            new GammaResolutionOutcome("yes-token", "Yes", 0, 1.00m),
            new GammaResolutionOutcome("no-token", "No", 1, 0.00m));
        result.Value.Winner.Should().Be(
            new GammaResolutionOutcome("yes-token", "Yes", 0, 1.00m));
    }

    [Fact]
    public async Task GetAsync_WithIntermediatePrices_ShouldReturnNonTerminalObservation()
    {
        var payload = CreatePayload((_, market) =>
            market["outcomePrices"] = "[\"0.01\",\"0.99\"]");
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(payload)));

        var result = await client.GetAsync(CreateRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(GammaTerminalResolutionStatus.NonTerminal);
        result.Value.Winner.Should().BeNull();
        result.Value.Outcomes.Should().Equal(
            new GammaResolutionOutcome("yes-token", "Yes", 0, 0.01m),
            new GammaResolutionOutcome("no-token", "No", 1, 0.99m));
    }

    [Fact]
    public async Task GetAsync_WithPartiallyUpdatedPrices_ShouldReturnNonTerminalObservation()
    {
        var payload = CreatePayload((_, market) =>
        {
            market["closed"] = false;
            market["acceptingOrders"] = true;
            market["outcomePrices"] = "[\"0.49\",\"0.50\"]";
        });
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(payload)));

        var result = await client.GetAsync(CreateRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(GammaTerminalResolutionStatus.NonTerminal);
        result.Value.Winner.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_WithoutUmaResolutionStatus_ShouldReturnNonTerminalObservation()
    {
        var payload = CreatePayload((_, market) => market.Remove("umaResolutionStatus"));
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(payload)));

        var result = await client.GetAsync(CreateRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(GammaTerminalResolutionStatus.NonTerminal);
        result.Value.UmaResolutionStatus.Should().BeNull();
        result.Value.Winner.Should().BeNull();
    }

    [Theory]
    [InlineData("[\"1\",\"1\"]", "gamma.terminal_resolution.winner.duplicate")]
    [InlineData("[\"0\",\"0\"]", "gamma.terminal_resolution.winner.missing")]
    [InlineData("[\"1\",\"0.2\"]", "gamma.terminal_resolution.winner.inconsistent")]
    public async Task GetAsync_WithImpossibleWinner_ShouldRejectResponse(
        string prices,
        string expectedCode)
    {
        var payload = CreatePayload((_, market) => market["outcomePrices"] = prices);
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(payload)));

        var result = await client.GetAsync(CreateRequest(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(expectedCode);
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("acceptingOrders")]
    [InlineData("umaResolutionStatus")]
    public async Task GetAsync_WithoutAllTerminalFlags_ShouldReturnNonTerminalObservation(
        string field)
    {
        var payload = CreatePayload((_, market) => market[field] = field switch
        {
            "closed" => false,
            "acceptingOrders" => true,
            _ => "proposed"
        });
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(payload)));

        var result = await client.GetAsync(CreateRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(GammaTerminalResolutionStatus.NonTerminal);
        result.Value.Winner.Should().BeNull();
    }

    [Theory]
    [InlineData("eventId", "id")]
    [InlineData("eventSlug", "slug")]
    [InlineData("marketId", "markets[0].id")]
    [InlineData("marketSlug", "markets[0].slug")]
    [InlineData("conditionId", "markets[0].conditionId")]
    [InlineData("tokenId", "markets[0].clobTokenIds[0]")]
    [InlineData("outcome", "markets[0].outcomes[0]")]
    public async Task GetAsync_WithIdentityMismatch_ShouldRejectResponse(
        string identityPart,
        string expectedField)
    {
        var payload = CreatePayload((eventValues, market) =>
        {
            switch (identityPart)
            {
                case "eventId":
                    eventValues["id"] = "other-event";
                    break;
                case "eventSlug":
                    eventValues["slug"] = "other-event";
                    break;
                case "marketId":
                    market["id"] = "other-market";
                    break;
                case "marketSlug":
                    market["slug"] = "other-market";
                    break;
                case "conditionId":
                    market["conditionId"] = "0xother";
                    break;
                case "tokenId":
                    market["clobTokenIds"] = "[\"other-token\",\"no-token\"]";
                    break;
                case "outcome":
                    market["outcomes"] = "[\"Other\",\"No\"]";
                    break;
            }
        });
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(payload)));

        var result = await client.GetAsync(CreateRequest(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gamma.terminal_resolution.identity_mismatch");
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.InvalidField.Should().Be(expectedField);
    }

    [Theory]
    [InlineData("missingPrices", "gamma.terminal_resolution.field.required")]
    [InlineData("invalidEmbeddedJson", "gamma.terminal_resolution.embedded_json.invalid")]
    [InlineData("countMismatch", "gamma.terminal_resolution.outcome_count_mismatch")]
    [InlineData("invalidPrice", "gamma.terminal_resolution.price.invalid")]
    [InlineData("outOfRangePrice", "gamma.terminal_resolution.price.out_of_range")]
    public async Task GetAsync_WithMalformedPayload_ShouldReturnExpectedError(
        string scenario,
        string expectedCode)
    {
        var payload = CreatePayload((_, market) =>
        {
            switch (scenario)
            {
                case "missingPrices":
                    market.Remove("outcomePrices");
                    break;
                case "invalidEmbeddedJson":
                    market["outcomePrices"] = "[invalid";
                    break;
                case "countMismatch":
                    market["outcomePrices"] = "[\"1\"]";
                    break;
                case "invalidPrice":
                    market["outcomePrices"] = "[\"winner\",\"0\"]";
                    break;
                case "outOfRangePrice":
                    market["outcomePrices"] = "[\"1.1\",\"-0.1\"]";
                    break;
            }
        });
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(payload)));

        var result = await client.GetAsync(CreateRequest(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(expectedCode);
    }

    [Fact]
    public async Task GetAsync_WithDuplicateExpectedToken_ShouldNotSendRequest()
    {
        var requestSent = false;
        var request = CreateRequest() with
        {
            Tokens =
            [
                new GammaResolutionTokenIdentity("same-token", "Yes", 0),
                new GammaResolutionTokenIdentity("same-token", "No", 1)
            ]
        };
        var client = CreateClient((_, _) =>
        {
            requestSent = true;
            return Task.FromResult(JsonResponse(CreatePayload()));
        });

        var result = await client.GetAsync(request, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(
            "gamma.terminal_resolution.request.token_id.duplicate");
        requestSent.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_WithNullExpectedToken_ShouldReturnRequestError()
    {
        var request = CreateRequest() with
        {
            Tokens =
            [
                null!,
                new GammaResolutionTokenIdentity("no-token", "No", 1)
            ]
        };
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(CreatePayload())));

        var result = await client.GetAsync(request, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(
            "gamma.terminal_resolution.request.field.required");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "gamma.terminal_resolution.not_found", ErrorType.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "gamma.terminal_resolution.http_error", ErrorType.Failure)]
    public async Task GetAsync_WithHttpError_ShouldReturnExpectedError(
        HttpStatusCode statusCode,
        string expectedCode,
        ErrorType expectedType)
    {
        var client = CreateClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(statusCode)));

        var result = await client.GetAsync(CreateRequest(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(expectedCode);
        result.Error.Type.Should().Be(expectedType);
    }

    [Fact]
    public async Task GetAsync_WithInvalidJson_ShouldReturnInvalidJsonError()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse("{invalid")));

        var result = await client.GetAsync(CreateRequest(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gamma.terminal_resolution.invalid_json");
    }

    [Fact]
    public async Task GetAsync_WhenRequestTimesOut_ShouldReturnTimeoutError()
    {
        var client = CreateClient((_, _) => throw new TaskCanceledException());

        var result = await client.GetAsync(CreateRequest(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gamma.terminal_resolution.timeout");
    }

    [Fact]
    public async Task GetAsync_WithNetworkFailure_ShouldReturnNetworkError()
    {
        var client = CreateClient((_, _) =>
            throw new HttpRequestException("DNS failure"));

        var result = await client.GetAsync(CreateRequest(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gamma.terminal_resolution.network");
    }

    [Fact]
    public async Task GetAsync_WhenResponseBodyTimesOut_ShouldReturnTimeoutError()
    {
        var client = CreateClient(
            (_, _) => Task.FromResult(StreamResponse(new BlockingReadStream())),
            TimeSpan.FromMilliseconds(20));

        var result = await client.GetAsync(CreateRequest(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gamma.terminal_resolution.timeout");
    }

    [Fact]
    public async Task GetAsync_WhenResponseBodyReadFails_ShouldReturnNetworkError()
    {
        var client = CreateClient((_, _) => Task.FromResult(
            StreamResponse(new FailingReadStream())));

        var result = await client.GetAsync(CreateRequest(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gamma.terminal_resolution.network");
    }

    [Fact]
    public async Task GetAsync_WhenCallerCancels_ShouldPropagateCancellation()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        var client = CreateClient((_, token) =>
            Task.FromCanceled<HttpResponseMessage>(token));

        var action = () => client.GetAsync(CreateRequest(), cancellationTokenSource.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private static GammaTerminalResolutionClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync,
        TimeSpan? requestTimeout = null)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(sendAsync));
        return requestTimeout.HasValue
            ? new GammaTerminalResolutionClient(
                httpClient,
                new FixedTimeProvider(ObservedAt),
                requestTimeout.Value)
            : new GammaTerminalResolutionClient(httpClient, new FixedTimeProvider(ObservedAt));
    }

    private static GammaTerminalResolutionRequest CreateRequest() => new(
        "event-id",
        "event/with space",
        "market-id",
        "market-slug",
        "0xcondition",
        [
            new GammaResolutionTokenIdentity("yes-token", "Yes", 0),
            new GammaResolutionTokenIdentity("no-token", "No", 1)
        ]);

    private static string CreatePayload(
        Action<Dictionary<string, object?>, Dictionary<string, object?>>? configure = null)
    {
        var market = new Dictionary<string, object?>
        {
            ["id"] = "market-id",
            ["slug"] = "market-slug",
            ["conditionId"] = "0xcondition",
            ["closed"] = true,
            ["acceptingOrders"] = false,
            ["umaResolutionStatus"] = "resolved",
            ["closedTime"] = "2026-08-27T12:00:00Z",
            ["outcomes"] = "[\"Yes\",\"No\"]",
            ["clobTokenIds"] = "[\"yes-token\",\"no-token\"]",
            ["outcomePrices"] = "[\"1\",\"0\"]"
        };
        var eventValues = new Dictionary<string, object?>
        {
            ["id"] = "event-id",
            ["slug"] = "event/with space",
            ["markets"] = new[] { market }
        };
        configure?.Invoke(eventValues, market);

        return JsonSerializer.Serialize(eventValues);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage StreamResponse(Stream stream)
    {
        var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            sendAsync(request, cancellationToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class BlockingReadStream : Stream
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
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
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
