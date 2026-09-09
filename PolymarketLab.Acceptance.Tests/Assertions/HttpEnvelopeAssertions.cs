using System.Text.Json.Nodes;
using FluentAssertions;

namespace PolymarketLab.Acceptance.Tests.Assertions;

internal static class HttpEnvelopeAssertions
{
    public static async Task<JsonObject> ReadEnvelopeAsync(this HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        var envelope = JsonNode.Parse(content) as JsonObject;
        envelope.Should().NotBeNull($"the response should be a JSON Envelope, but was: {content}");
        return envelope!;
    }

    public static IReadOnlyCollection<string> ErrorCodes(this JsonObject envelope) =>
        envelope["listErrors"]!
            .AsArray()
            .Select(error => error!["errorCode"]!.GetValue<string>())
            .ToArray();
}
