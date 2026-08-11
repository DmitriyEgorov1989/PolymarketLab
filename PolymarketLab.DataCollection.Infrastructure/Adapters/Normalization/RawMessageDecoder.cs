using System.Text;
using System.Text.Json;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Ports;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;

public sealed class RawMessageDecoder : IRawMessageDecoder
{
    private const string InvalidUtf8Code = "normalization.payload.utf8.invalid";
    private const string InvalidJsonCode = "normalization.payload.json.invalid";
    private const string InvalidRootKindCode = "normalization.payload.root_kind.invalid";
    private const string InvalidItemKindCode = "normalization.payload.item_kind.invalid";

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public RawMessageDecodeResult Decode(RawMessageEnvelope message)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            StrictUtf8.GetCharCount(message.Payload.Span);
        }
        catch (DecoderFallbackException)
        {
            return RawMessageDecodeResult.Invalid(new NormalizationIssue(
                InvalidUtf8Code,
                "Raw message payload is not valid UTF-8."));
        }

        try
        {
            using var document = JsonDocument.Parse(message.Payload);

            return document.RootElement.ValueKind switch
            {
                JsonValueKind.Object => RawMessageDecodeResult.Decoded(
                    [RawMessageItemDecodeResult.Decoded(0, document.RootElement)]),
                JsonValueKind.Array => DecodeArray(document.RootElement),
                _ => RawMessageDecodeResult.Invalid(new NormalizationIssue(
                    InvalidRootKindCode,
                    "Raw message JSON root must be an object or an array.",
                    "$"))
            };
        }
        catch (JsonException)
        {
            return RawMessageDecodeResult.Invalid(new NormalizationIssue(
                InvalidJsonCode,
                "Raw message payload is not valid JSON."));
        }
    }

    private static RawMessageDecodeResult DecodeArray(JsonElement array)
    {
        var items = new List<RawMessageItemDecodeResult>(array.GetArrayLength());
        var rawItemIndex = 0;

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                items.Add(RawMessageItemDecodeResult.Decoded(rawItemIndex, item));
            }
            else
            {
                items.Add(RawMessageItemDecodeResult.Invalid(
                    rawItemIndex,
                    new NormalizationIssue(
                        InvalidItemKindCode,
                        "Raw message array item must be a JSON object.",
                        $"$[{rawItemIndex}]")));
            }

            rawItemIndex++;
        }

        return RawMessageDecodeResult.Decoded(items);
    }
}
