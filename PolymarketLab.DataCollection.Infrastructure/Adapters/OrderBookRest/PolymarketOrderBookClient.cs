using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.OrderBookRest;

internal sealed class PolymarketOrderBookClient : IOrderBookSnapshotSource
{
    private const string OrderBookEndpoint = "https://clob.polymarket.com/book?token_id=";
    private const NumberStyles DecimalStyles =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _requestTimeout;

    public PolymarketOrderBookClient(HttpClient httpClient)
        : this(httpClient, DefaultRequestTimeout)
    {
    }

    internal PolymarketOrderBookClient(HttpClient httpClient, TimeSpan requestTimeout)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (requestTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));

        _httpClient = httpClient;
        _requestTimeout = requestTimeout;
    }

    public async Task<Result<OrderBookSnapshot, Error>> GetAsync(
        string assetId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assetId))
            return Errors.RequiredField("assetId");

        var endpoint = OrderBookEndpoint + Uri.EscapeDataString(assetId);
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_requestTimeout);
        var requestToken = timeoutSource.Token;

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                requestToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Errors.Timeout;
        }
        catch (HttpRequestException)
        {
            return Errors.Network;
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.BadRequest)
                return Errors.InvalidAssetId(assetId);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return Errors.NotFound(assetId);
            if (!response.IsSuccessStatusCode)
                return Errors.HttpError(response.StatusCode);

            ExternalOrderBookSnapshotDto? dto;
            try
            {
                dto = await response.Content.ReadFromJsonAsync<ExternalOrderBookSnapshotDto>(
                    cancellationToken: requestToken);
            }
            catch (JsonException)
            {
                return Errors.InvalidJson;
            }
            catch (NotSupportedException)
            {
                return Errors.InvalidJson;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Errors.Timeout;
            }
            catch (HttpRequestException)
            {
                return Errors.Network;
            }
            catch (IOException)
            {
                return Errors.Network;
            }

            return dto is null
                ? Errors.InvalidJson
                : Map(dto, assetId);
        }
    }

    private static Result<OrderBookSnapshot, Error> Map(
        ExternalOrderBookSnapshotDto dto,
        string requestedAssetId)
    {
        if (string.IsNullOrWhiteSpace(dto.Market))
            return Errors.RequiredField("market");
        if (string.IsNullOrWhiteSpace(dto.AssetId))
            return Errors.RequiredField("asset_id");
        if (!string.Equals(dto.AssetId, requestedAssetId, StringComparison.Ordinal))
            return Errors.AssetIdMismatch(requestedAssetId, dto.AssetId);
        if (string.IsNullOrWhiteSpace(dto.Hash))
            return Errors.RequiredField("hash");
        if (dto.Bids is null)
            return Errors.RequiredField("bids");
        if (dto.Asks is null)
            return Errors.RequiredField("asks");
        if (!dto.NegativeRisk.HasValue)
            return Errors.RequiredField("neg_risk");

        var timestamp = ParseTimestamp(dto.Timestamp, "timestamp");
        if (timestamp.IsFailure)
            return timestamp.Error;

        var minimumOrderSize = ParseDecimal(dto.MinimumOrderSize, "min_order_size");
        if (minimumOrderSize.IsFailure)
            return minimumOrderSize.Error;
        if (minimumOrderSize.Value <= 0)
            return Errors.PositiveDecimalRequired("min_order_size");

        var tickSize = ParseDecimal(dto.TickSize, "tick_size");
        if (tickSize.IsFailure)
            return tickSize.Error;
        if (tickSize.Value <= 0)
            return Errors.PositiveDecimalRequired("tick_size");

        var lastTradePrice = ParsePrice(dto.LastTradePrice, "last_trade_price");
        if (lastTradePrice.IsFailure)
            return lastTradePrice.Error;

        var bids = MapLevels(dto.Bids, "bids");
        if (bids.IsFailure)
            return bids.Error;

        var asks = MapLevels(dto.Asks, "asks");
        if (asks.IsFailure)
            return asks.Error;

        return new OrderBookSnapshot(
            dto.Market,
            dto.AssetId,
            timestamp.Value,
            dto.Hash,
            bids.Value,
            asks.Value,
            minimumOrderSize.Value,
            tickSize.Value,
            dto.NegativeRisk.Value,
            lastTradePrice.Value);
    }

    private static Result<IReadOnlyCollection<OrderBookSnapshotLevel>, Error> MapLevels(
        IReadOnlyList<ExternalOrderBookLevelDto?> levels,
        string field)
    {
        var mapped = new OrderBookSnapshotLevel[levels.Count];
        for (var index = 0; index < levels.Count; index++)
        {
            var level = levels[index];
            if (level is null)
                return Errors.RequiredField($"{field}[{index}]");

            var price = ParsePrice(level.Price, $"{field}[{index}].price");
            if (price.IsFailure)
                return price.Error;

            var size = ParseDecimal(level.Size, $"{field}[{index}].size");
            if (size.IsFailure)
                return size.Error;
            if (size.Value < 0)
                return Errors.NonNegativeDecimalRequired($"{field}[{index}].size");

            mapped[index] = new OrderBookSnapshotLevel(price.Value, size.Value);
        }

        return mapped;
    }

    private static Result<decimal, Error> ParsePrice(string? text, string field)
    {
        var value = ParseDecimal(text, field);
        if (value.IsFailure)
            return value.Error;

        return value.Value is < 0 or > 1
            ? Errors.PriceOutOfRange(field)
            : value.Value;
    }

    private static Result<decimal, Error> ParseDecimal(string? text, string field)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Errors.RequiredField(field);

        return decimal.TryParse(
            text,
            DecimalStyles,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : Errors.InvalidDecimal(field);
    }

    private static Result<long, Error> ParseTimestamp(string? text, string field)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Errors.RequiredField(field);

        return long.TryParse(
                   text,
                   NumberStyles.AllowLeadingSign,
                   CultureInfo.InvariantCulture,
                   out var value)
               && value >= 0
            ? value
            : Errors.InvalidTimestamp(field);
    }

    private static class Errors
    {
        public static Error InvalidAssetId(string assetId) => new(
            "clob.order_book.asset_id.invalid",
            $"The CLOB API rejected asset ID '{assetId}'.",
            ErrorType.ValueIsInvalid,
            "assetId");

        public static Error NotFound(string assetId) => new(
            "clob.order_book.not_found",
            $"No CLOB order book exists for asset ID '{assetId}'.",
            ErrorType.NotFound,
            "assetId");

        public static Error Timeout => new(
            "clob.order_book.timeout",
            "The CLOB order book request timed out.",
            ErrorType.Failure);

        public static Error Network => new(
            "clob.order_book.network",
            "The CLOB order book request failed due to a network error.",
            ErrorType.Failure);

        public static Error HttpError(HttpStatusCode statusCode) => new(
            "clob.order_book.http_error",
            $"The CLOB API returned HTTP status code {(int)statusCode}.",
            ErrorType.Failure);

        public static Error InvalidJson => new(
            "clob.order_book.invalid_json",
            "The CLOB API returned invalid order book JSON.",
            ErrorType.ValueIsInvalid);

        public static Error RequiredField(string field) => new(
            "clob.order_book.field.required",
            $"The CLOB order book field '{field}' is required.",
            ErrorType.ValueIsRequired,
            field);

        public static Error InvalidDecimal(string field) => new(
            "clob.order_book.field.decimal.invalid",
            $"The CLOB order book field '{field}' must be a valid invariant decimal string.",
            ErrorType.ValueIsInvalid,
            field);

        public static Error PositiveDecimalRequired(string field) => new(
            "clob.order_book.field.positive_required",
            $"The CLOB order book field '{field}' must be positive.",
            ErrorType.ValueIsInvalid,
            field);

        public static Error NonNegativeDecimalRequired(string field) => new(
            "clob.order_book.field.non_negative_required",
            $"The CLOB order book field '{field}' cannot be negative.",
            ErrorType.ValueIsInvalid,
            field);

        public static Error PriceOutOfRange(string field) => new(
            "clob.order_book.field.price_out_of_range",
            $"The CLOB order book field '{field}' must be between zero and one.",
            ErrorType.ValueIsInvalid,
            field);

        public static Error InvalidTimestamp(string field) => new(
            "clob.order_book.field.timestamp.invalid",
            $"The CLOB order book field '{field}' must be a non-negative epoch-millisecond string.",
            ErrorType.ValueIsInvalid,
            field);

        public static Error AssetIdMismatch(string requested, string actual) => new(
            "clob.order_book.asset_id.mismatch",
            $"The CLOB API returned asset ID '{actual}' for requested asset ID '{requested}'.",
            ErrorType.ValueIsInvalid,
            "asset_id");
    }
}
