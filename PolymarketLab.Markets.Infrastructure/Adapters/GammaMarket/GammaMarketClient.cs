using CSharpFunctionalExtensions;
using PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects;
using PolymarketLab.Markets.Core.Ports;
using PolymarketLab.Markets.Core.Ports.Dto;
using PolymarketLab.SharedKernel.Errors;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PolymarketLab.Markets.Infrastructure.Adapters.GammaMarket
{
    public sealed class GammaMarketClient(HttpClient httpClient) : IExternalMarketGateway
    {
        private const string MarketsBySlugEndpoint = "https://gamma-api.polymarket.com/markets/slug/";

        public async Task<Result<ExternalMarket, Error>> GetBySlugAsync(
            MarketSlug slug,
            CancellationToken cancellationToken)
        {
            var endpoint = MarketsBySlugEndpoint + Uri.EscapeDataString(slug.Value);
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return GammaMarketErrors.Timeout;
            }
            catch (HttpRequestException)
            {
                return GammaMarketErrors.Network;
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return GammaMarketErrors.NotFound(slug.Value);

                if (!response.IsSuccessStatusCode)
                    return GammaMarketErrors.HttpError(response.StatusCode);

                GammaMarketDto? gammaMarket;
                try
                {
                    gammaMarket = await response.Content.ReadFromJsonAsync<GammaMarketDto>(
                        cancellationToken: cancellationToken);
                }
                catch (JsonException)
                {
                    return GammaMarketErrors.InvalidJson;
                }
                catch (NotSupportedException)
                {
                    return GammaMarketErrors.InvalidJson;
                }

                if (gammaMarket is null)
                    return GammaMarketErrors.InvalidJson;

                if (string.IsNullOrWhiteSpace(gammaMarket.Id))
                    return GammaMarketErrors.RequiredField("id");

                if (string.IsNullOrWhiteSpace(gammaMarket.Slug))
                    return GammaMarketErrors.RequiredField("slug");

                if (string.IsNullOrWhiteSpace(gammaMarket.Question))
                    return GammaMarketErrors.RequiredField("question");

                if (string.IsNullOrWhiteSpace(gammaMarket.ConditionId))
                    return GammaMarketErrors.RequiredField("conditionId");

                if (gammaMarket.Active is null)
                    return GammaMarketErrors.RequiredField("active");

                if (gammaMarket.Closed is null)
                    return GammaMarketErrors.RequiredField("closed");

                if (gammaMarket.EnableOrderBook is null)
                    return GammaMarketErrors.RequiredField("enableOrderBook");

                if (string.IsNullOrWhiteSpace(gammaMarket.Outcomes))
                    return GammaMarketErrors.RequiredField("outcomes");

                if (string.IsNullOrWhiteSpace(gammaMarket.ClobTokenIds))
                    return GammaMarketErrors.RequiredField("clobTokenIds");

                if (!gammaMarket.EnableOrderBook.Value)
                    return GammaMarketErrors.OrderBookDisabled;

                string?[]? outcomes;
                string?[]? tokenIds;
                try
                {
                    outcomes = JsonSerializer.Deserialize<string?[]>(gammaMarket.Outcomes);
                    tokenIds = JsonSerializer.Deserialize<string?[]>(gammaMarket.ClobTokenIds);
                }
                catch (JsonException)
                {
                    return GammaMarketErrors.InvalidJson;
                }

                if (outcomes is null || tokenIds is null)
                    return GammaMarketErrors.InvalidJson;

                if (outcomes.Length != tokenIds.Length)
                    return GammaMarketErrors.TokenCountMismatch(outcomes.Length, tokenIds.Length);

                var tokens = new ExternalMarketToken[outcomes.Length];
                for (var index = 0; index < outcomes.Length; index++)
                {
                    if (string.IsNullOrWhiteSpace(outcomes[index]))
                        return GammaMarketErrors.RequiredField($"outcomes[{index}]");

                    if (string.IsNullOrWhiteSpace(tokenIds[index]))
                        return GammaMarketErrors.EmptyTokenId(index);

                    tokens[index] = new ExternalMarketToken(outcomes[index]!, tokenIds[index]!, index);
                }

                return new ExternalMarket(
                    gammaMarket.Id,
                    gammaMarket.Slug,
                    gammaMarket.Question,
                    gammaMarket.ConditionId,
                    gammaMarket.StartDate,
                    gammaMarket.EndDate,
                    gammaMarket.Active.Value,
                    gammaMarket.Closed.Value,
                    gammaMarket.EnableOrderBook.Value,
                    tokens);
            }
        }

        private sealed record GammaMarketDto(
            [property: JsonPropertyName("id")] string? Id,
            [property: JsonPropertyName("slug")] string? Slug,
            [property: JsonPropertyName("question")] string? Question,
            [property: JsonPropertyName("conditionId")] string? ConditionId,
            [property: JsonPropertyName("startDate")] DateTimeOffset? StartDate,
            [property: JsonPropertyName("endDate")] DateTimeOffset? EndDate,
            [property: JsonPropertyName("active")] bool? Active,
            [property: JsonPropertyName("closed")] bool? Closed,
            [property: JsonPropertyName("enableOrderBook")] bool? EnableOrderBook,
            [property: JsonPropertyName("outcomes")] string? Outcomes,
            [property: JsonPropertyName("clobTokenIds")] string? ClobTokenIds);

        private static class GammaMarketErrors
        {
            public static Error NotFound(string slug) => new(
                "gamma.market.not_found",
                $"Gamma market with slug '{slug}' was not found.",
                ErrorType.NotFound);

            public static Error Timeout => new(
                "gamma.market.timeout",
                "The Gamma API request timed out.",
                ErrorType.Failure);

            public static Error Network => new(
                "gamma.market.network",
                "The Gamma API request failed due to a network error.",
                ErrorType.Failure);

            public static Error HttpError(HttpStatusCode statusCode) => new(
                "gamma.market.http_error",
                $"The Gamma API returned HTTP status code {(int)statusCode}.",
                ErrorType.Failure);

            public static Error InvalidJson => new(
                "gamma.market.invalid_json",
                "The Gamma API returned invalid JSON.",
                ErrorType.ValueIsInvalid);

            public static Error RequiredField(string field) => new(
                "gamma.market.field.required",
                $"The Gamma market field '{field}' is required.",
                ErrorType.ValueIsRequired,
                field);

            public static Error TokenCountMismatch(int outcomeCount, int tokenCount) => new(
                "gamma.market.token_count_mismatch",
                $"Gamma market has {outcomeCount} outcomes and {tokenCount} token IDs.",
                ErrorType.InvalidSize);

            public static Error EmptyTokenId(int index) => new(
                "gamma.market.token_id.empty",
                $"Gamma market token ID at index {index} is empty.",
                ErrorType.ValueIsRequired,
                $"clobTokenIds[{index}]");

            public static Error OrderBookDisabled => new(
                "gamma.market.order_book.disabled",
                "The Gamma market order book is disabled.",
                ErrorType.Conflict);
        }
    }
}