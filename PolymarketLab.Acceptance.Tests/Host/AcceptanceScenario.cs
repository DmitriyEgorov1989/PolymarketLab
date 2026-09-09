using System.Net;
using System.Text;
using System.Text.Json;

namespace PolymarketLab.Acceptance.Tests.Host;

internal sealed class AcceptanceScenario(DateTimeOffset eventStartsAt)
{
    public const string EventSlug = "acceptance-event";
    public const string EventId = "event-acceptance";
    public const string MarketId = "market-acceptance";
    public const string MarketSlug = "acceptance-market";
    public const string ConditionId = "0xacceptance";
    public const string YesTokenId = "token-yes";
    public const string NoTokenId = "token-no";

    public DateTimeOffset EventStartsAt { get; } = eventStartsAt;
    public DateTimeOffset EventEndsAt { get; } = eventStartsAt.AddMinutes(5);
    public bool IsResolved { get; set; }
    public bool ClobSelectsNo { get; set; }

    public HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var uri = request.RequestUri
            ?? throw new InvalidOperationException("External HTTP request URI is missing.");

        if (uri.Host == "gamma-api.polymarket.com")
            return JsonResponse(CreateGammaPayload());

        if (uri.Host == "clob.polymarket.com" && uri.AbsolutePath == "/book")
        {
            var tokenId = uri.Query.Contains(NoTokenId, StringComparison.Ordinal)
                ? NoTokenId
                : YesTokenId;
            return JsonResponse(CreateOrderBookPayload(tokenId));
        }

        if (uri.Host == "clob.polymarket.com" && uri.AbsolutePath.StartsWith("/markets/", StringComparison.Ordinal))
            return JsonResponse(CreateClobResolutionPayload());

        throw new InvalidOperationException(
            $"Unexpected external HTTP request: {request.Method} {uri.GetLeftPart(UriPartial.Path)}");
    }

    private string CreateGammaPayload()
    {
        var market = new Dictionary<string, object?>
        {
            ["id"] = MarketId,
            ["slug"] = MarketSlug,
            ["question"] = "Will the acceptance market resolve Yes?",
            ["conditionId"] = ConditionId,
            ["createdAt"] = EventStartsAt.AddDays(-1),
            ["acceptingOrdersTimestamp"] = EventStartsAt.AddMinutes(-10),
            ["startDate"] = EventStartsAt.AddMinutes(-5),
            ["eventStartTime"] = EventStartsAt,
            ["endDate"] = EventEndsAt,
            ["closedTime"] = IsResolved ? EventEndsAt : null,
            ["umaResolutionStatus"] = IsResolved ? "resolved" : "pending",
            ["active"] = true,
            ["closed"] = IsResolved,
            ["acceptingOrders"] = !IsResolved,
            ["enableOrderBook"] = true,
            ["outcomes"] = "[\"Yes\",\"No\"]",
            ["clobTokenIds"] = $"[\"{YesTokenId}\",\"{NoTokenId}\"]",
            ["outcomePrices"] = IsResolved ? "[\"1\",\"0\"]" : "[\"0.5\",\"0.5\"]"
        };
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = EventId,
            ["slug"] = EventSlug,
            ["markets"] = new[] { market }
        });
    }

    private static string CreateOrderBookPayload(string tokenId) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["market"] = ConditionId,
            ["asset_id"] = tokenId,
            ["timestamp"] = "1788600000000",
            ["hash"] = $"hash-{tokenId}",
            ["bids"] = new[] { new { price = "0.49", size = "10" } },
            ["asks"] = new[] { new { price = "0.51", size = "10" } },
            ["min_order_size"] = "1",
            ["tick_size"] = "0.01",
            ["neg_risk"] = false,
            ["last_trade_price"] = "0.50"
        });

    private string CreateClobResolutionPayload()
    {
        var yesWins = !ClobSelectsNo;
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["condition_id"] = ConditionId,
            ["closed"] = IsResolved,
            ["accepting_orders"] = !IsResolved,
            ["tokens"] = new object[]
            {
                new
                {
                    token_id = YesTokenId,
                    outcome = "Yes",
                    price = yesWins ? 1m : 0m,
                    winner = yesWins
                },
                new
                {
                    token_id = NoTokenId,
                    outcome = "No",
                    price = yesWins ? 0m : 1m,
                    winner = !yesWins
                }
            }
        });
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
}
