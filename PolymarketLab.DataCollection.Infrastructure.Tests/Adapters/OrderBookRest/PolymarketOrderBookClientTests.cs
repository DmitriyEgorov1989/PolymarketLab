using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using PolymarketLab.DataCollection.Infrastructure.Adapters.OrderBookRest;
using PolymarketLab.SharedKernel.Errors;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.OrderBookRest;

public sealed class PolymarketOrderBookClientTests
{
    [Fact]
    public async Task GetAsync_WithEmptyAssetId_ShouldNotSendRequest()
    {
        var requestSent = false;
        var client = CreateClient((_, _) =>
        {
            requestSent = true;
            return Task.FromResult(JsonResponse(CreatePayload("asset")));
        });

        var result = await client.GetAsync(" ", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("clob.order_book.field.required");
        result.Error.InvalidField.Should().Be("assetId");
        requestSent.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_WithValidResponse_ShouldRequestAndMapOfficialContract()
    {
        HttpMethod? requestMethod = null;
        Uri? requestUri = null;
        const string assetId = "asset/with space";
        var client = CreateClient((request, _) =>
        {
            requestMethod = request.Method;
            requestUri = request.RequestUri;
            return Task.FromResult(JsonResponse(CreatePayload(assetId)));
        });

        var result = await client.GetAsync(assetId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        requestMethod.Should().Be(HttpMethod.Get);
        requestUri!.AbsoluteUri.Should()
            .Be("https://clob.polymarket.com/book?token_id=asset%2Fwith%20space");
        result.Value.MarketConditionId.Should().Be("0xcondition");
        result.Value.AssetId.Should().Be(assetId);
        result.Value.SourceTimestamp.Should().Be(1_765_000_000_123);
        result.Value.Hash.Should().Be("snapshot-hash");
        result.Value.Bids.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { Price = 0.45m, Size = 100.125m });
        result.Value.Asks.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { Price = 0.46m, Size = 150m });
        result.Value.MinimumOrderSize.Should().Be(1m);
        result.Value.TickSize.Should().Be(0.01m);
        result.Value.NegativeRisk.Should().BeFalse();
        result.Value.LastTradePrice.Should().Be(0.455m);
    }

    [Fact]
    public async Task GetAsync_WithEmptySides_ShouldReturnEmptyCollections()
    {
        var payload = CreatePayload("asset", values =>
        {
            values["bids"] = Array.Empty<object>();
            values["asks"] = Array.Empty<object>();
        });
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(payload)));

        var result = await client.GetAsync("asset", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Bids.Should().BeEmpty();
        result.Value.Asks.Should().BeEmpty();
    }

    [Theory]
    [InlineData("market")]
    [InlineData("asset_id")]
    [InlineData("timestamp")]
    [InlineData("hash")]
    [InlineData("bids")]
    [InlineData("asks")]
    [InlineData("min_order_size")]
    [InlineData("tick_size")]
    [InlineData("neg_risk")]
    [InlineData("last_trade_price")]
    public async Task GetAsync_WithMissingRequiredField_ShouldReturnFieldError(string field)
    {
        var payload = CreatePayload("asset", values => values.Remove(field));
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(payload)));

        var result = await client.GetAsync("asset", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("clob.order_book.field.required");
        result.Error.InvalidField.Should().Be(field);
    }

    [Theory]
    [InlineData("bids", "price", "1.01", "clob.order_book.field.price_out_of_range")]
    [InlineData("asks", "size", "-1", "clob.order_book.field.non_negative_required")]
    [InlineData("bids", "price", "not-a-number", "clob.order_book.field.decimal.invalid")]
    public async Task GetAsync_WithInvalidLevel_ShouldReturnFieldError(
        string side,
        string property,
        string value,
        string expectedCode)
    {
        var payload = CreatePayload("asset", values =>
        {
            values[side] = new[]
            {
                new Dictionary<string, string>
                {
                    ["price"] = property == "price" ? value : "0.5",
                    ["size"] = property == "size" ? value : "10"
                }
            };
        });
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(payload)));

        var result = await client.GetAsync("asset", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(expectedCode);
        result.Error.InvalidField.Should().Be($"{side}[0].{property}");
    }

    [Fact]
    public async Task GetAsync_WithDifferentResponseAsset_ShouldRejectSnapshot()
    {
        var client = CreateClient((_, _) =>
            Task.FromResult(JsonResponse(CreatePayload("different-asset"))));

        var result = await client.GetAsync("requested-asset", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("clob.order_book.asset_id.mismatch");
        result.Error.InvalidField.Should().Be("asset_id");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "clob.order_book.asset_id.invalid", ErrorType.ValueIsInvalid)]
    [InlineData(HttpStatusCode.NotFound, "clob.order_book.not_found", ErrorType.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "clob.order_book.http_error", ErrorType.Failure)]
    public async Task GetAsync_WithHttpError_ShouldReturnExpectedError(
        HttpStatusCode statusCode,
        string expectedCode,
        ErrorType expectedType)
    {
        var client = CreateClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(statusCode)));

        var result = await client.GetAsync("asset", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(expectedCode);
        result.Error.Type.Should().Be(expectedType);
    }

    [Fact]
    public async Task GetAsync_WithInvalidJson_ShouldReturnInvalidJsonError()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse("{invalid")));

        var result = await client.GetAsync("asset", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("clob.order_book.invalid_json");
    }

    [Fact]
    public async Task GetAsync_WhenRequestTimesOut_ShouldReturnTimeoutError()
    {
        var client = CreateClient((_, _) => throw new TaskCanceledException());

        var result = await client.GetAsync("asset", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("clob.order_book.timeout");
    }

    [Fact]
    public async Task GetAsync_WithNetworkFailure_ShouldReturnNetworkError()
    {
        var client = CreateClient((_, _) => throw new HttpRequestException("DNS failure"));

        var result = await client.GetAsync("asset", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("clob.order_book.network");
    }

    [Fact]
    public async Task GetAsync_WhenResponseBodyTimesOut_ShouldReturnTimeoutError()
    {
        var client = CreateClient(
            (_, _) => Task.FromResult(StreamResponse(new BlockingReadStream())),
            TimeSpan.FromMilliseconds(20));

        var result = await client.GetAsync("asset", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("clob.order_book.timeout");
    }

    [Fact]
    public async Task GetAsync_WhenResponseBodyReadFails_ShouldReturnNetworkError()
    {
        var client = CreateClient((_, _) => Task.FromResult(
            StreamResponse(new FailingReadStream())));

        var result = await client.GetAsync("asset", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("clob.order_book.network");
    }

    [Fact]
    public async Task GetAsync_WhenCallerCancels_ShouldPropagateCancellation()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        var client = CreateClient((_, token) =>
            Task.FromCanceled<HttpResponseMessage>(token));

        var action = () => client.GetAsync("asset", cancellationTokenSource.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private static PolymarketOrderBookClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync,
        TimeSpan? requestTimeout = null)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(sendAsync));
        return requestTimeout.HasValue
            ? new PolymarketOrderBookClient(httpClient, requestTimeout.Value)
            : new PolymarketOrderBookClient(httpClient);
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
        var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static string CreatePayload(
        string assetId,
        Action<Dictionary<string, object?>>? configure = null)
    {
        var values = new Dictionary<string, object?>
        {
            ["market"] = "0xcondition",
            ["asset_id"] = assetId,
            ["timestamp"] = "1765000000123",
            ["hash"] = "snapshot-hash",
            ["bids"] = new[] { new { price = "0.45", size = "100.125" } },
            ["asks"] = new[] { new { price = "0.46", size = "150" } },
            ["min_order_size"] = "1",
            ["tick_size"] = "0.01",
            ["neg_risk"] = false,
            ["last_trade_price"] = "0.455"
        };
        configure?.Invoke(values);

        return JsonSerializer.Serialize(values);
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
