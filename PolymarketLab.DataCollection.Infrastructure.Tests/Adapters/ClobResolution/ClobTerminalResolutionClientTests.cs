using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.ClobResolution;
using PolymarketLab.SharedKernel.Errors;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.ClobResolution;

public sealed class ClobTerminalResolutionClientTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 8, 31, 12, 5, 2, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_WithExactTerminalSettlement_ShouldReturnWinner()
    {
        var client = CreateClient((request, _) =>
        {
            request.Method.Should().Be(HttpMethod.Get);
            request.RequestUri.Should().Be(
                "https://clob.polymarket.com/markets/0xcondition%2Fwith%20space");

            return Task.FromResult(JsonResponse(
                """
                {
                  "condition_id": "0xcondition/with space",
                  "closed": true,
                  "accepting_orders": false,
                  "tokens": [
                    { "token_id": "yes-token", "outcome": "Yes", "price": 1.0, "winner": true },
                    { "token_id": "no-token", "outcome": "No", "price": 0.0, "winner": false }
                  ]
                }
                """));
        });

        var result = await client.GetAsync(CreateRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ObservedAt.Should().Be(ObservedAt);
        result.Value.ConditionId.Should().Be("0xcondition/with space");
        result.Value.Closed.Should().BeTrue();
        result.Value.AcceptingOrders.Should().BeFalse();
        result.Value.Status.Should().Be(ClobTerminalResolutionStatus.Terminal);
        result.Value.Outcomes.Should().Equal(
            new ClobResolutionOutcome("yes-token", "Yes", 0, 1m),
            new ClobResolutionOutcome("no-token", "No", 1, 0m));
        result.Value.Winner.Should().Be(result.Value.Outcomes.First());
    }

    [Fact]
    public async Task GetAsync_WhenMarketIsOpen_ShouldReturnOrderedNonTerminalObservation()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(
            """
            {
              "condition_id": "0xcondition/with space",
              "closed": false,
              "accepting_orders": true,
              "tokens": [
                { "token_id": "no-token", "outcome": "No", "price": 0.01, "winner": false },
                { "token_id": "yes-token", "outcome": "Yes", "price": 0.99, "winner": false }
              ]
            }
            """)));

        var result = await client.GetAsync(CreateRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ClobTerminalResolutionStatus.NonTerminal);
        result.Value.Outcomes.Select(outcome => outcome.TokenId).Should().Equal(
            "yes-token",
            "no-token");
        result.Value.Winner.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_WithIntermediateSettlementPrices_ShouldReturnNonTerminalObservation()
    {
        var payload = CreatePayload((_, tokens) =>
        {
            tokens[0]["price"] = 0.99m;
            tokens[0]["winner"] = false;
            tokens[1]["price"] = 0.01m;
        });
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(payload)));

        var result = await client.GetAsync(CreateRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ClobTerminalResolutionStatus.NonTerminal);
        result.Value.Winner.Should().BeNull();
    }

    [Theory]
    [InlineData("condition", "condition_id")]
    [InlineData("token", "tokens")]
    [InlineData("outcome", "tokens[0].outcome")]
    public async Task GetAsync_WithIdentityMismatch_ShouldRejectResponse(
        string identityPart,
        string expectedField)
    {
        var payload = CreatePayload((market, tokens) =>
        {
            switch (identityPart)
            {
                case "condition":
                    market["condition_id"] = "0xother";
                    break;
                case "token":
                    tokens[0]["token_id"] = "other-token";
                    break;
                case "outcome":
                    tokens[0]["outcome"] = "Other";
                    break;
            }
        });
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(payload)));

        var result = await client.GetAsync(CreateRequest(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("clob.terminal_resolution.identity_mismatch");
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.InvalidField.Should().Be(expectedField);
    }

    [Theory]
    [InlineData("duplicateWinner", "clob.terminal_resolution.winner.duplicate")]
    [InlineData("missingWinner", "clob.terminal_resolution.winner.missing")]
    [InlineData("winnerPrice", "clob.terminal_resolution.winner.inconsistent")]
    [InlineData("loserPrice", "clob.terminal_resolution.winner.inconsistent")]
    public async Task GetAsync_WithInvalidTerminalSettlement_ShouldRejectResponse(
        string scenario,
        string expectedCode)
    {
        var payload = CreatePayload((_, tokens) =>
        {
            switch (scenario)
            {
                case "duplicateWinner":
                    tokens[1]["price"] = 1m;
                    tokens[1]["winner"] = true;
                    break;
                case "missingWinner":
                    tokens[0]["price"] = 0m;
                    tokens[0]["winner"] = false;
                    break;
                case "winnerPrice":
                    tokens[0]["price"] = 0.99m;
                    break;
                case "loserPrice":
                    tokens[1]["price"] = 0.01m;
                    break;
            }
        });
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(payload)));

        var result = await client.GetAsync(CreateRequest(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(expectedCode);
    }

    [Theory]
    [InlineData("missingTokens", "clob.terminal_resolution.field.required")]
    [InlineData("nullToken", "clob.terminal_resolution.field.required")]
    [InlineData("duplicateToken", "clob.terminal_resolution.token_id.duplicate")]
    [InlineData("missingPrice", "clob.terminal_resolution.field.required")]
    [InlineData("priceBelowRange", "clob.terminal_resolution.price.out_of_range")]
    [InlineData("priceAboveRange", "clob.terminal_resolution.price.out_of_range")]
    public async Task GetAsync_WithInvalidPayload_ShouldReturnExpectedError(
        string scenario,
        string expectedCode)
    {
        var payload = CreatePayload((market, tokens) =>
        {
            switch (scenario)
            {
                case "missingTokens":
                    market.Remove("tokens");
                    break;
                case "nullToken":
                    ((object?[])market["tokens"]!)[0] = null;
                    break;
                case "duplicateToken":
                    tokens[1]["token_id"] = "yes-token";
                    break;
                case "missingPrice":
                    tokens[0].Remove("price");
                    break;
                case "priceBelowRange":
                    tokens[0]["price"] = -0.01m;
                    break;
                case "priceAboveRange":
                    tokens[0]["price"] = 1.01m;
                    break;
            }
        });
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(payload)));

        var result = await client.GetAsync(CreateRequest(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(expectedCode);
    }

    [Theory]
    [InlineData("conditionRequired", "clob.terminal_resolution.request.field.required")]
    [InlineData("tokensRequired", "clob.terminal_resolution.request.field.required")]
    [InlineData("tokensEmpty", "clob.terminal_resolution.request.tokens.empty")]
    [InlineData("indexes", "clob.terminal_resolution.request.outcome_indexes.invalid")]
    [InlineData("duplicateToken", "clob.terminal_resolution.request.token_id.duplicate")]
    [InlineData("duplicateOutcome", "clob.terminal_resolution.request.outcome.duplicate")]
    public async Task GetAsync_WithInvalidRequest_ShouldNotSendHttpRequest(
        string scenario,
        string expectedCode)
    {
        var request = scenario switch
        {
            "conditionRequired" => CreateRequest() with { ConditionId = " " },
            "tokensRequired" => CreateRequest() with { Tokens = null! },
            "tokensEmpty" => CreateRequest() with { Tokens = [] },
            "indexes" => CreateRequest() with
            {
                Tokens =
                [
                    new ClobResolutionTokenIdentity("yes-token", "Yes", 0),
                    new ClobResolutionTokenIdentity("no-token", "No", 2)
                ]
            },
            "duplicateToken" => CreateRequest() with
            {
                Tokens =
                [
                    new ClobResolutionTokenIdentity("same-token", "Yes", 0),
                    new ClobResolutionTokenIdentity("same-token", "No", 1)
                ]
            },
            _ => CreateRequest() with
            {
                Tokens =
                [
                    new ClobResolutionTokenIdentity("yes-token", "Same", 0),
                    new ClobResolutionTokenIdentity("no-token", "Same", 1)
                ]
            }
        };
        var requestSent = false;
        var client = CreateClient((_, _) =>
        {
            requestSent = true;
            return Task.FromResult(JsonResponse(CreatePayload()));
        });

        var result = await client.GetAsync(request, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(expectedCode);
        requestSent.Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "clob.terminal_resolution.not_found", ErrorType.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "clob.terminal_resolution.http_error", ErrorType.Failure)]
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
        result.Error.Code.Should().Be("clob.terminal_resolution.invalid_json");
    }

    [Fact]
    public async Task GetAsync_WhenRequestTimesOut_ShouldReturnTimeoutError()
    {
        var client = CreateClient((_, _) => throw new TaskCanceledException());

        var result = await client.GetAsync(CreateRequest(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("clob.terminal_resolution.timeout");
    }

    [Fact]
    public async Task GetAsync_WithNetworkFailure_ShouldReturnNetworkError()
    {
        var client = CreateClient((_, _) => throw new HttpRequestException("DNS failure"));

        var result = await client.GetAsync(CreateRequest(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("clob.terminal_resolution.network");
    }

    [Fact]
    public async Task GetAsync_WhenResponseBodyTimesOut_ShouldReturnTimeoutError()
    {
        var client = CreateClient(
            (_, _) => Task.FromResult(StreamResponse(new BlockingReadStream())),
            TimeSpan.FromMilliseconds(20));

        var result = await client.GetAsync(CreateRequest(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("clob.terminal_resolution.timeout");
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

    private static ClobTerminalResolutionClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync,
        TimeSpan? requestTimeout = null)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(sendAsync));
        return requestTimeout.HasValue
            ? new ClobTerminalResolutionClient(
                httpClient,
                new FixedTimeProvider(ObservedAt),
                requestTimeout.Value)
            : new ClobTerminalResolutionClient(httpClient, new FixedTimeProvider(ObservedAt));
    }

    private static ClobTerminalResolutionRequest CreateRequest() => new(
        "0xcondition/with space",
        [
            new ClobResolutionTokenIdentity("yes-token", "Yes", 0),
            new ClobResolutionTokenIdentity("no-token", "No", 1)
        ]);

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static string CreatePayload(
        Action<Dictionary<string, object?>, Dictionary<string, object?>[]>? configure = null)
    {
        var tokens = new[]
        {
            new Dictionary<string, object?>
            {
                ["token_id"] = "yes-token",
                ["outcome"] = "Yes",
                ["price"] = 1m,
                ["winner"] = true
            },
            new Dictionary<string, object?>
            {
                ["token_id"] = "no-token",
                ["outcome"] = "No",
                ["price"] = 0m,
                ["winner"] = false
            }
        };
        var market = new Dictionary<string, object?>
        {
            ["condition_id"] = "0xcondition/with space",
            ["closed"] = true,
            ["accepting_orders"] = false,
            ["tokens"] = tokens.Cast<object?>().ToArray()
        };
        configure?.Invoke(market, tokens);
        return JsonSerializer.Serialize(market);
    }

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
}
