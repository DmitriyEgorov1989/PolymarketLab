using System.Text.Json;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Core.Application.Normalization;

public sealed class NormalizationDispatcher : INormalizationDispatcher
{
    private const string EventTypeField = "event_type";
    private readonly Dictionary<string, IRawMessageNormalizer> _normalizers;

    public NormalizationDispatcher(IEnumerable<IRawMessageNormalizer> normalizers)
    {
        ArgumentNullException.ThrowIfNull(normalizers);

        _normalizers = new Dictionary<string, IRawMessageNormalizer>(StringComparer.Ordinal);

        foreach (var normalizer in normalizers)
        {
            if (normalizer is null)
                throw new InvalidOperationException("Normalizer collection cannot contain null.");

            var eventType = normalizer.EventType;
            if (string.IsNullOrWhiteSpace(eventType))
                throw new InvalidOperationException(
                    $"Normalizer '{normalizer.GetType().Name}' must declare an event type.");

            if (normalizer.Version <= 0)
                throw new InvalidOperationException(
                    $"Normalizer '{normalizer.GetType().Name}' must declare a positive version.");

            if (!_normalizers.TryAdd(eventType, normalizer))
            {
                var registeredNormalizer = _normalizers[eventType];
                throw new InvalidOperationException(
                    $"Event type '{eventType}' is declared by both " +
                    $"'{registeredNormalizer.GetType().Name}' and '{normalizer.GetType().Name}'.");
            }
        }
    }

    public NormalizationResult Dispatch(LogicalRawEvent rawEvent)
    {
        ArgumentNullException.ThrowIfNull(rawEvent);

        if (!rawEvent.Json.TryGetProperty(EventTypeField, out var eventTypeElement) ||
            eventTypeElement.ValueKind == JsonValueKind.Null)
        {
            return RequiredEventType(rawEvent.RawItemIndex);
        }

        if (eventTypeElement.ValueKind != JsonValueKind.String)
        {
            return NormalizationResult.Invalid(
                rawEvent.RawItemIndex,
                new NormalizationIssue(
                    "normalization.event_type.invalid",
                    "Event type must be a string.",
                    EventTypeField));
        }

        var eventType = eventTypeElement.GetString();
        if (string.IsNullOrWhiteSpace(eventType))
            return RequiredEventType(rawEvent.RawItemIndex);

        if (!_normalizers.TryGetValue(eventType, out var normalizer))
        {
            return NormalizationResult.Unsupported(
                rawEvent.RawItemIndex,
                new NormalizationIssue(
                    "normalization.event_type.unsupported",
                    "Event type is not supported.",
                    EventTypeField));
        }

        return normalizer.Normalize(rawEvent);
    }

    private static NormalizationResult RequiredEventType(int rawItemIndex)
    {
        return NormalizationResult.Invalid(
            rawItemIndex,
            new NormalizationIssue(
                "normalization.event_type.required",
                "Event type is required.",
                EventTypeField));
    }
}
