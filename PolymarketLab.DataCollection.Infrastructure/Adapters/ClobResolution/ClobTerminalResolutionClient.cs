using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.ClobResolution;

internal sealed class ClobTerminalResolutionClient : IClobTerminalResolutionSource
{
    private const string MarketsEndpoint = "https://clob.polymarket.com/markets/";
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _requestTimeout;

    public ClobTerminalResolutionClient(HttpClient httpClient)
        : this(httpClient, TimeProvider.System, DefaultRequestTimeout)
    {
    }

    internal ClobTerminalResolutionClient(HttpClient httpClient, TimeProvider timeProvider)
        : this(httpClient, timeProvider, DefaultRequestTimeout)
    {
    }

    internal ClobTerminalResolutionClient(
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

    public async Task<Result<ClobTerminalResolutionObservation, Error>> GetAsync(
        ClobTerminalResolutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestError = ValidateRequest(request);
        if (requestError is not null)
            return requestError;

        var endpoint = MarketsEndpoint + Uri.EscapeDataString(request.ConditionId);
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
                return Errors.NotFound(request.ConditionId);
            if (!response.IsSuccessStatusCode)
                return Errors.HttpError(response.StatusCode);

            ClobTerminalResolutionDto? dto;
            try
            {
                dto = await response.Content.ReadFromJsonAsync<ClobTerminalResolutionDto>(
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

    private static Error? ValidateRequest(ClobTerminalResolutionRequest request)
    {
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
            if (orderedTokens[index].OutcomeIndex != index)
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

    private static Result<ClobTerminalResolutionObservation, Error> Map(
        ClobTerminalResolutionDto dto,
        ClobTerminalResolutionRequest request,
        DateTimeOffset observedAt)
    {
        if (string.IsNullOrWhiteSpace(dto.ConditionId))
            return Errors.RequiredField("condition_id");
        if (!string.Equals(dto.ConditionId, request.ConditionId, StringComparison.Ordinal))
            return Errors.IdentityMismatch("condition_id", request.ConditionId, dto.ConditionId);
        if (dto.Closed is null)
            return Errors.RequiredField("closed");
        if (dto.AcceptingOrders is null)
            return Errors.RequiredField("accepting_orders");
        if (dto.Tokens is null)
            return Errors.RequiredField("tokens");

        var outcomesResult = MapOutcomes(dto.Tokens, request.Tokens);
        if (outcomesResult.IsFailure)
            return outcomesResult.Error;

        var outcomes = outcomesResult.Value.Outcomes;
        var hasTerminalFlags = dto.Closed.Value && !dto.AcceptingOrders.Value;
        if (!hasTerminalFlags)
        {
            return new ClobTerminalResolutionObservation(
                observedAt,
                dto.ConditionId,
                dto.Closed.Value,
                dto.AcceptingOrders.Value,
                ClobTerminalResolutionStatus.NonTerminal,
                outcomes,
                null);
        }

        var winnerIndexes = outcomesResult.Value.WinnerIndexes;
        if (winnerIndexes.Length > 1)
            return Errors.DuplicateWinner;
        if (winnerIndexes.Length == 0)
        {
            if (outcomes.Any(outcome => outcome.Price is > 0m and < 1m))
            {
                return new ClobTerminalResolutionObservation(
                    observedAt,
                    dto.ConditionId,
                    dto.Closed.Value,
                    dto.AcceptingOrders.Value,
                    ClobTerminalResolutionStatus.NonTerminal,
                    outcomes,
                    null);
            }

            return Errors.MissingWinner;
        }

        var winnerIndex = winnerIndexes[0];
        if (outcomes[winnerIndex].Price != 1m
            || outcomes.Where((_, index) => index != winnerIndex).Any(outcome => outcome.Price != 0m))
            return Errors.InconsistentWinner;

        return new ClobTerminalResolutionObservation(
            observedAt,
            dto.ConditionId,
            dto.Closed.Value,
            dto.AcceptingOrders.Value,
            ClobTerminalResolutionStatus.Terminal,
            outcomes,
            outcomes[winnerIndex]);
    }

    private static Result<MappedOutcomes, Error> MapOutcomes(
        ClobTerminalResolutionTokenDto?[] tokens,
        IReadOnlyCollection<ClobResolutionTokenIdentity> expectedTokens)
    {
        if (tokens.Length != expectedTokens.Count)
            return Errors.IdentityTokenCountMismatch(expectedTokens.Count, tokens.Length);

        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token is null)
                return Errors.RequiredField($"tokens[{index}]");
            if (string.IsNullOrWhiteSpace(token.TokenId))
                return Errors.RequiredField($"tokens[{index}].token_id");
            if (string.IsNullOrWhiteSpace(token.Outcome))
                return Errors.RequiredField($"tokens[{index}].outcome");
            if (token.Price is null)
                return Errors.RequiredField($"tokens[{index}].price");
            if (token.Winner is null)
                return Errors.RequiredField($"tokens[{index}].winner");
            if (token.Price is < 0m or > 1m)
                return Errors.PriceOutOfRange(index);
        }

        if (tokens.Select(token => token!.TokenId).Distinct(StringComparer.Ordinal).Count()
            != tokens.Length)
            return Errors.DuplicateTokenId;

        var tokensById = tokens
            .Select(token => token!)
            .ToDictionary(token => token.TokenId!, StringComparer.Ordinal);
        var orderedExpectedTokens = expectedTokens
            .OrderBy(token => token.OutcomeIndex)
            .ToArray();
        var outcomes = new ClobResolutionOutcome[orderedExpectedTokens.Length];
        var winnerIndexes = new List<int>();

        for (var index = 0; index < orderedExpectedTokens.Length; index++)
        {
            var expected = orderedExpectedTokens[index];
            if (!tokensById.TryGetValue(expected.TokenId, out var token))
                return Errors.IdentityMismatch("tokens", expected.TokenId, "missing");
            if (!string.Equals(token.Outcome, expected.Outcome, StringComparison.Ordinal))
                return Errors.IdentityMismatch(
                    $"tokens[{index}].outcome",
                    expected.Outcome,
                    token.Outcome!);

            outcomes[index] = new ClobResolutionOutcome(
                token.TokenId!,
                token.Outcome!,
                expected.OutcomeIndex,
                token.Price!.Value);
            if (token.Winner == true)
                winnerIndexes.Add(index);
        }

        return new MappedOutcomes(outcomes, winnerIndexes.ToArray());
    }

    private sealed record MappedOutcomes(
        ClobResolutionOutcome[] Outcomes,
        int[] WinnerIndexes);

    private static class Errors
    {
        public static Error NotFound(string conditionId) => new(
            "clob.terminal_resolution.not_found",
            $"CLOB market with condition ID '{conditionId}' was not found for terminal resolution.",
            ErrorType.NotFound);

        public static Error Timeout => new(
            "clob.terminal_resolution.timeout",
            "The CLOB terminal resolution request timed out.",
            ErrorType.Failure);

        public static Error Network => new(
            "clob.terminal_resolution.network",
            "The CLOB terminal resolution request failed due to a network error.",
            ErrorType.Failure);

        public static Error HttpError(HttpStatusCode statusCode) => new(
            "clob.terminal_resolution.http_error",
            $"The CLOB API returned HTTP status code {(int)statusCode} for terminal resolution.",
            ErrorType.Failure);

        public static Error InvalidJson => new(
            "clob.terminal_resolution.invalid_json",
            "The CLOB API returned invalid terminal resolution JSON.",
            ErrorType.ValueIsInvalid);

        public static Error RequiredRequestField(string field) => new(
            "clob.terminal_resolution.request.field.required",
            $"The terminal resolution request field '{field}' is required.",
            ErrorType.ValueIsRequired,
            field);

        public static Error InvalidRequestTokenCount => new(
            "clob.terminal_resolution.request.tokens.empty",
            "The terminal resolution request must contain at least one token.",
            ErrorType.CollectionIsTooSmall,
            "tokens");

        public static Error InvalidRequestOutcomeIndexes => new(
            "clob.terminal_resolution.request.outcome_indexes.invalid",
            "Terminal resolution request outcome indexes must be unique and contiguous from zero.",
            ErrorType.ValueIsInvalid,
            "tokens");

        public static Error DuplicateRequestTokenId => new(
            "clob.terminal_resolution.request.token_id.duplicate",
            "Terminal resolution request token IDs must be unique.",
            ErrorType.Conflict,
            "tokens");

        public static Error DuplicateRequestOutcome => new(
            "clob.terminal_resolution.request.outcome.duplicate",
            "Terminal resolution request outcomes must be unique.",
            ErrorType.Conflict,
            "tokens");

        public static Error RequiredField(string field) => new(
            "clob.terminal_resolution.field.required",
            $"The CLOB terminal resolution field '{field}' is required.",
            ErrorType.ValueIsRequired,
            field);

        public static Error IdentityMismatch(string field, string expected, string actual) => new(
            "clob.terminal_resolution.identity_mismatch",
            $"CLOB terminal resolution field '{field}' was expected to be '{expected}', but was '{actual}'.",
            ErrorType.Conflict,
            field);

        public static Error IdentityTokenCountMismatch(int expected, int actual) => new(
            "clob.terminal_resolution.identity_token_count_mismatch",
            $"CLOB terminal resolution was expected to have {expected} tokens, but had {actual}.",
            ErrorType.Conflict,
            "tokens");

        public static Error DuplicateTokenId => new(
            "clob.terminal_resolution.token_id.duplicate",
            "CLOB terminal resolution token IDs must be unique.",
            ErrorType.ValueIsInvalid,
            "tokens");

        public static Error PriceOutOfRange(int index) => new(
            "clob.terminal_resolution.price.out_of_range",
            $"CLOB terminal resolution price at index {index} must be between zero and one.",
            ErrorType.ValueIsInvalid,
            $"tokens[{index}].price");

        public static Error DuplicateWinner => new(
            "clob.terminal_resolution.winner.duplicate",
            "CLOB terminal resolution contains more than one winner.",
            ErrorType.ValueIsInvalid,
            "tokens");

        public static Error MissingWinner => new(
            "clob.terminal_resolution.winner.missing",
            "CLOB terminal resolution does not contain a winner.",
            ErrorType.ValueIsInvalid,
            "tokens");

        public static Error InconsistentWinner => new(
            "clob.terminal_resolution.winner.inconsistent",
            "CLOB terminal resolution winner must have price 1.00 and every loser must have price 0.00.",
            ErrorType.ValueIsInvalid,
            "tokens");
    }
}
