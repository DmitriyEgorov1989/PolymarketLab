using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.GammaResolution;

internal sealed class GammaTerminalResolutionClient : IGammaTerminalResolutionSource
{
    private const string EventsBySlugEndpoint =
        "https://gamma-api.polymarket.com/events/slug/";
    private const NumberStyles DecimalStyles =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _requestTimeout;

    public GammaTerminalResolutionClient(HttpClient httpClient)
        : this(httpClient, TimeProvider.System, DefaultRequestTimeout)
    {
    }

    internal GammaTerminalResolutionClient(HttpClient httpClient, TimeProvider timeProvider)
        : this(httpClient, timeProvider, DefaultRequestTimeout)
    {
    }

    internal GammaTerminalResolutionClient(
        HttpClient httpClient,
        TimeProvider timeProvider,
        TimeSpan requestTimeout)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (requestTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));

        _httpClient = httpClient;
        _timeProvider = timeProvider;
        _requestTimeout = requestTimeout;
    }

    public async Task<Result<GammaTerminalResolutionObservation, Error>> GetAsync(
        GammaTerminalResolutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestError = ValidateRequest(request);
        if (requestError is not null)
            return requestError;

        var endpoint = EventsBySlugEndpoint + Uri.EscapeDataString(request.EventSlug);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, endpoint);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_requestTimeout);
        var requestToken = timeoutSource.Token;

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                httpRequest,
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
            if (response.StatusCode == HttpStatusCode.NotFound)
                return Errors.NotFound(request.EventSlug);
            if (!response.IsSuccessStatusCode)
                return Errors.HttpError(response.StatusCode);

            GammaTerminalResolutionEventDto? dto;
            try
            {
                dto = await response.Content
                    .ReadFromJsonAsync<GammaTerminalResolutionEventDto>(
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
                : Map(dto, request, _timeProvider.GetUtcNow());
        }
    }

    private static Error? ValidateRequest(GammaTerminalResolutionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ExternalEventId))
            return Errors.RequiredRequestField("externalEventId");
        if (string.IsNullOrWhiteSpace(request.EventSlug))
            return Errors.RequiredRequestField("eventSlug");
        if (string.IsNullOrWhiteSpace(request.ExternalMarketId))
            return Errors.RequiredRequestField("externalMarketId");
        if (string.IsNullOrWhiteSpace(request.MarketSlug))
            return Errors.RequiredRequestField("marketSlug");
        if (string.IsNullOrWhiteSpace(request.ConditionId))
            return Errors.RequiredRequestField("conditionId");
        if (request.Tokens is null)
            return Errors.RequiredRequestField("tokens");
        if (request.Tokens.Count == 0)
            return Errors.InvalidRequestTokenCount;

        var tokens = request.Tokens.ToArray();
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token is null)
                return Errors.RequiredRequestField($"tokens[{index}]");
            if (string.IsNullOrWhiteSpace(token.TokenId))
                return Errors.RequiredRequestField($"tokens[{index}].tokenId");
            if (string.IsNullOrWhiteSpace(token.Outcome))
                return Errors.RequiredRequestField($"tokens[{index}].outcome");
        }

        var orderedTokens = tokens.OrderBy(token => token.OutcomeIndex).ToArray();
        for (var index = 0; index < orderedTokens.Length; index++)
        {
            var token = orderedTokens[index];
            if (token.OutcomeIndex != index)
                return Errors.InvalidRequestOutcomeIndexes;
        }

        if (orderedTokens.Select(token => token.TokenId).Distinct(StringComparer.Ordinal).Count()
            != orderedTokens.Length)
            return Errors.DuplicateRequestTokenId;
        if (orderedTokens.Select(token => token.Outcome).Distinct(StringComparer.Ordinal).Count()
            != orderedTokens.Length)
            return Errors.DuplicateRequestOutcome;

        return null;
    }

    private static Result<GammaTerminalResolutionObservation, Error> Map(
        GammaTerminalResolutionEventDto dto,
        GammaTerminalResolutionRequest request,
        DateTimeOffset observedAt)
    {
        if (string.IsNullOrWhiteSpace(dto.Id))
            return Errors.RequiredField("id");
        if (string.IsNullOrWhiteSpace(dto.Slug))
            return Errors.RequiredField("slug");
        if (!string.Equals(dto.Id, request.ExternalEventId, StringComparison.Ordinal))
            return Errors.IdentityMismatch("id", request.ExternalEventId, dto.Id);
        if (!string.Equals(dto.Slug, request.EventSlug, StringComparison.Ordinal))
            return Errors.IdentityMismatch("slug", request.EventSlug, dto.Slug);
        if (dto.Markets is null)
            return Errors.RequiredField("markets");
        if (dto.Markets.Length != 1)
            return Errors.InvalidMarketCount(dto.Markets.Length);

        var market = dto.Markets[0];
        if (market is null)
            return Errors.RequiredField("markets[0]");
        if (string.IsNullOrWhiteSpace(market.Id))
            return Errors.RequiredField("markets[0].id");
        if (string.IsNullOrWhiteSpace(market.Slug))
            return Errors.RequiredField("markets[0].slug");
        if (string.IsNullOrWhiteSpace(market.ConditionId))
            return Errors.RequiredField("markets[0].conditionId");
        if (!string.Equals(market.Id, request.ExternalMarketId, StringComparison.Ordinal))
            return Errors.IdentityMismatch(
                "markets[0].id",
                request.ExternalMarketId,
                market.Id);
        if (!string.Equals(market.Slug, request.MarketSlug, StringComparison.Ordinal))
            return Errors.IdentityMismatch(
                "markets[0].slug",
                request.MarketSlug,
                market.Slug);
        if (!string.Equals(market.ConditionId, request.ConditionId, StringComparison.Ordinal))
            return Errors.IdentityMismatch(
                "markets[0].conditionId",
                request.ConditionId,
                market.ConditionId);
        if (market.Closed is null)
            return Errors.RequiredField("markets[0].closed");
        if (market.AcceptingOrders is null)
            return Errors.RequiredField("markets[0].acceptingOrders");

        var outcomesResult = MapOutcomes(market, request.Tokens);
        if (outcomesResult.IsFailure)
            return outcomesResult.Error;

        var outcomes = outcomesResult.Value;
        var winnerCandidates = outcomes.Where(outcome => outcome.Price == 1m).ToArray();
        if (winnerCandidates.Length > 1)
            return Errors.DuplicateWinner;

        var hasTerminalFlags = market.Closed.Value
            && !market.AcceptingOrders.Value
            && string.Equals(
                market.UmaResolutionStatus,
                "resolved",
                StringComparison.OrdinalIgnoreCase);

        if (winnerCandidates.Length == 1
            && outcomes.Any(outcome => outcome != winnerCandidates[0] && outcome.Price != 0m))
            return Errors.InconsistentWinner;

        if (hasTerminalFlags
            && winnerCandidates.Length == 0
            && outcomes.All(outcome => outcome.Price == 0m))
            return Errors.MissingWinner;

        if (!hasTerminalFlags || winnerCandidates.Length == 0)
        {
            return new GammaTerminalResolutionObservation(
                observedAt,
                dto.Id,
                dto.Slug,
                market.Id,
                market.Slug,
                market.ConditionId,
                market.Closed.Value,
                market.AcceptingOrders.Value,
                market.UmaResolutionStatus,
                market.ClosedTime,
                GammaTerminalResolutionStatus.NonTerminal,
                outcomes,
                null);
        }

        var winner = winnerCandidates[0];
        return new GammaTerminalResolutionObservation(
            observedAt,
            dto.Id,
            dto.Slug,
            market.Id,
            market.Slug,
            market.ConditionId,
            market.Closed.Value,
            market.AcceptingOrders.Value,
            market.UmaResolutionStatus,
            market.ClosedTime,
            GammaTerminalResolutionStatus.Terminal,
            outcomes,
            winner);
    }

    private static Result<IReadOnlyCollection<GammaResolutionOutcome>, Error> MapOutcomes(
        GammaTerminalResolutionMarketDto market,
        IReadOnlyCollection<GammaResolutionTokenIdentity> expectedTokens)
    {
        if (string.IsNullOrWhiteSpace(market.Outcomes))
            return Errors.RequiredField("markets[0].outcomes");
        if (string.IsNullOrWhiteSpace(market.ClobTokenIds))
            return Errors.RequiredField("markets[0].clobTokenIds");
        if (string.IsNullOrWhiteSpace(market.OutcomePrices))
            return Errors.RequiredField("markets[0].outcomePrices");

        string?[]? outcomes;
        string?[]? tokenIds;
        string?[]? prices;
        try
        {
            outcomes = JsonSerializer.Deserialize<string?[]>(market.Outcomes);
            tokenIds = JsonSerializer.Deserialize<string?[]>(market.ClobTokenIds);
            prices = JsonSerializer.Deserialize<string?[]>(market.OutcomePrices);
        }
        catch (JsonException)
        {
            return Errors.InvalidEmbeddedJson;
        }

        if (outcomes is null || tokenIds is null || prices is null)
            return Errors.InvalidEmbeddedJson;
        if (outcomes.Length != tokenIds.Length || outcomes.Length != prices.Length)
            return Errors.OutcomeCountMismatch(outcomes.Length, tokenIds.Length, prices.Length);

        var orderedExpectedTokens = expectedTokens
            .OrderBy(token => token.OutcomeIndex)
            .ToArray();
        if (outcomes.Length != orderedExpectedTokens.Length)
            return Errors.IdentityTokenCountMismatch(orderedExpectedTokens.Length, outcomes.Length);

        var mapped = new GammaResolutionOutcome[outcomes.Length];
        for (var index = 0; index < outcomes.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(outcomes[index]))
                return Errors.RequiredField($"markets[0].outcomes[{index}]");
            if (string.IsNullOrWhiteSpace(tokenIds[index]))
                return Errors.RequiredField($"markets[0].clobTokenIds[{index}]");
            if (string.IsNullOrWhiteSpace(prices[index]))
                return Errors.RequiredField($"markets[0].outcomePrices[{index}]");

            var expected = orderedExpectedTokens[index];
            if (!string.Equals(tokenIds[index], expected.TokenId, StringComparison.Ordinal))
                return Errors.IdentityMismatch(
                    $"markets[0].clobTokenIds[{index}]",
                    expected.TokenId,
                    tokenIds[index]!);
            if (!string.Equals(outcomes[index], expected.Outcome, StringComparison.Ordinal))
                return Errors.IdentityMismatch(
                    $"markets[0].outcomes[{index}]",
                    expected.Outcome,
                    outcomes[index]!);
            if (!decimal.TryParse(
                    prices[index],
                    DecimalStyles,
                    CultureInfo.InvariantCulture,
                    out var price))
                return Errors.InvalidPrice(index);
            if (price is < 0m or > 1m)
                return Errors.PriceOutOfRange(index);

            mapped[index] = new GammaResolutionOutcome(
                tokenIds[index]!,
                outcomes[index]!,
                index,
                price);
        }

        return mapped;
    }

    private static class Errors
    {
        public static Error NotFound(string eventSlug) => new(
            "gamma.terminal_resolution.not_found",
            $"Gamma event with slug '{eventSlug}' was not found for terminal resolution.",
            ErrorType.NotFound);

        public static Error Timeout => new(
            "gamma.terminal_resolution.timeout",
            "The Gamma terminal resolution request timed out.",
            ErrorType.Failure);

        public static Error Network => new(
            "gamma.terminal_resolution.network",
            "The Gamma terminal resolution request failed due to a network error.",
            ErrorType.Failure);

        public static Error HttpError(HttpStatusCode statusCode) => new(
            "gamma.terminal_resolution.http_error",
            $"The Gamma API returned HTTP status code {(int)statusCode} for terminal resolution.",
            ErrorType.Failure);

        public static Error InvalidJson => new(
            "gamma.terminal_resolution.invalid_json",
            "The Gamma API returned invalid terminal resolution JSON.",
            ErrorType.ValueIsInvalid);

        public static Error InvalidEmbeddedJson => new(
            "gamma.terminal_resolution.embedded_json.invalid",
            "The Gamma terminal resolution arrays contain invalid JSON.",
            ErrorType.ValueIsInvalid);

        public static Error RequiredRequestField(string field) => new(
            "gamma.terminal_resolution.request.field.required",
            $"The terminal resolution request field '{field}' is required.",
            ErrorType.ValueIsRequired,
            field);

        public static Error InvalidRequestTokenCount => new(
            "gamma.terminal_resolution.request.tokens.empty",
            "The terminal resolution request must contain at least one token.",
            ErrorType.CollectionIsTooSmall,
            "tokens");

        public static Error InvalidRequestOutcomeIndexes => new(
            "gamma.terminal_resolution.request.outcome_indexes.invalid",
            "Terminal resolution request outcome indexes must be unique and contiguous from zero.",
            ErrorType.ValueIsInvalid,
            "tokens");

        public static Error DuplicateRequestTokenId => new(
            "gamma.terminal_resolution.request.token_id.duplicate",
            "Terminal resolution request token IDs must be unique.",
            ErrorType.Conflict,
            "tokens");

        public static Error DuplicateRequestOutcome => new(
            "gamma.terminal_resolution.request.outcome.duplicate",
            "Terminal resolution request outcomes must be unique.",
            ErrorType.Conflict,
            "tokens");

        public static Error RequiredField(string field) => new(
            "gamma.terminal_resolution.field.required",
            $"The Gamma terminal resolution field '{field}' is required.",
            ErrorType.ValueIsRequired,
            field);

        public static Error InvalidMarketCount(int count) => new(
            "gamma.terminal_resolution.market_count_invalid",
            $"Gamma terminal resolution event must contain exactly one market, but contained {count}.",
            ErrorType.InvalidSize,
            "markets");

        public static Error IdentityMismatch(string field, string expected, string actual) => new(
            "gamma.terminal_resolution.identity_mismatch",
            $"Gamma terminal resolution field '{field}' was expected to be '{expected}', but was '{actual}'.",
            ErrorType.Conflict,
            field);

        public static Error OutcomeCountMismatch(int outcomes, int tokens, int prices) => new(
            "gamma.terminal_resolution.outcome_count_mismatch",
            $"Gamma terminal resolution has {outcomes} outcomes, {tokens} token IDs and {prices} prices.",
            ErrorType.InvalidSize);

        public static Error IdentityTokenCountMismatch(int expected, int actual) => new(
            "gamma.terminal_resolution.identity_token_count_mismatch",
            $"Gamma terminal resolution was expected to have {expected} tokens, but had {actual}.",
            ErrorType.Conflict,
            "markets[0].clobTokenIds");

        public static Error InvalidPrice(int index) => new(
            "gamma.terminal_resolution.price.invalid",
            $"Gamma terminal resolution price at index {index} is not a valid invariant decimal string.",
            ErrorType.ValueIsInvalid,
            $"markets[0].outcomePrices[{index}]");

        public static Error PriceOutOfRange(int index) => new(
            "gamma.terminal_resolution.price.out_of_range",
            $"Gamma terminal resolution price at index {index} must be between zero and one.",
            ErrorType.ValueIsInvalid,
            $"markets[0].outcomePrices[{index}]");

        public static Error DuplicateWinner => new(
            "gamma.terminal_resolution.winner.duplicate",
            "Gamma terminal resolution contains more than one winner.",
            ErrorType.ValueIsInvalid,
            "markets[0].outcomePrices");

        public static Error MissingWinner => new(
            "gamma.terminal_resolution.winner.missing",
            "Gamma terminal resolution does not contain a winner.",
            ErrorType.ValueIsInvalid,
            "markets[0].outcomePrices");

        public static Error InconsistentWinner => new(
            "gamma.terminal_resolution.winner.inconsistent",
            "Gamma terminal resolution winner has price 1.00 while another outcome has a non-zero price.",
            ErrorType.ValueIsInvalid,
            "markets[0].outcomePrices");
    }
}
