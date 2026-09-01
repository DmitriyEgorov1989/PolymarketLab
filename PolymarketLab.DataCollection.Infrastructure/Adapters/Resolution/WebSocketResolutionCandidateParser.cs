using System.Text.Json;
using PolymarketLab.DataCollection.Core.Application.Resolution;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Resolution;

internal static class WebSocketResolutionCandidateParser
{
    public static IReadOnlyCollection<WebSocketResolutionCandidate> Parse(
        long rawMessageId,
        long connectionEpoch,
        DateTimeOffset receivedAt,
        byte[] payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var candidates = new List<WebSocketResolutionCandidate>();

            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    AddCandidate(candidates, item, index, rawMessageId, connectionEpoch, receivedAt);
                    index++;
                }
            }
            else
            {
                AddCandidate(candidates, document.RootElement, 0, rawMessageId, connectionEpoch, receivedAt);
            }

            return candidates;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void AddCandidate(
        List<WebSocketResolutionCandidate> candidates,
        JsonElement item,
        int rawItemIndex,
        long rawMessageId,
        long connectionEpoch,
        DateTimeOffset receivedAt)
    {
        if (item.ValueKind != JsonValueKind.Object
            || ReadString(item, "event_type") != "market_resolved")
        {
            return;
        }

        candidates.Add(new WebSocketResolutionCandidate(
            rawMessageId,
            rawItemIndex,
            connectionEpoch,
            receivedAt,
            ReadString(item, "id"),
            ReadString(item, "market"),
            ReadStringArray(item, "assets_ids"),
            ReadString(item, "winning_asset_id"),
            ReadString(item, "winning_outcome")));
    }

    private static string? ReadString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyCollection<string>? ReadStringArray(
        JsonElement item,
        string name)
    {
        if (!item.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var result = new List<string>(value.GetArrayLength());
        foreach (var element in value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
                return null;

            result.Add(element.GetString()!);
        }

        return result;
    }
}
