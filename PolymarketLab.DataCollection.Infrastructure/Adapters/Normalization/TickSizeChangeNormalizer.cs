using PolymarketLab.DataCollection.Core.Application.Normalization;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;

internal sealed class TickSizeChangeNormalizer : IRawMessageNormalizer
{
    public string EventType => "tick_size_change";
    public int Version => 1;

    public NormalizationResult Normalize(LogicalRawEvent rawEvent)
    {
        ArgumentNullException.ThrowIfNull(rawEvent);

        var marketResult = PolymarketJsonReader.ReadRequiredString(
            rawEvent.Json,
            PolymarketJsonFields.Market);
        if (marketResult.IsFailure)
            return Invalid(rawEvent, marketResult.Error);

        var assetIdResult = PolymarketJsonReader.ReadRequiredString(
            rawEvent.Json,
            PolymarketJsonFields.AssetId);
        if (assetIdResult.IsFailure)
            return Invalid(rawEvent, assetIdResult.Error);

        var oldTickSizeResult = PolymarketJsonReader.ReadRequiredDecimal(
            rawEvent.Json,
            PolymarketJsonFields.OldTickSize);
        if (oldTickSizeResult.IsFailure)
            return Invalid(rawEvent, oldTickSizeResult.Error);

        var newTickSizeResult = PolymarketJsonReader.ReadRequiredDecimal(
            rawEvent.Json,
            PolymarketJsonFields.NewTickSize);
        if (newTickSizeResult.IsFailure)
            return Invalid(rawEvent, newTickSizeResult.Error);

        if (newTickSizeResult.Value <= 0)
        {
            return Invalid(
                rawEvent,
                new NormalizationIssue(
                    "normalization.field.range.invalid",
                    "New tick size must be positive.",
                    PolymarketJsonFields.NewTickSize));
        }

        var timestampResult = PolymarketJsonReader.ReadOptionalEpochMilliseconds(
            rawEvent.Json,
            PolymarketJsonFields.Timestamp);
        if (timestampResult.IsFailure)
            return Invalid(rawEvent, timestampResult.Error);

        var normalizedEvent = new NormalizedEvent(
            rawMessageId: rawEvent.RawMessageId,
            rawItemIndex: rawEvent.RawItemIndex,
            projectionVersion: rawEvent.ProjectionVersion,
            normalizerVersion: Version,
            eventType: EventType,
            sessionId: rawEvent.SessionId,
            receivedAt: rawEvent.ReceivedAt,
            sourceTimestamp: timestampResult.Value,
            marketConditionId: marketResult.Value,
            assetId: assetIdResult.Value,
            records: [new TickSizeChangeRecord(oldTickSizeResult.Value, newTickSizeResult.Value)]);

        return NormalizationResult.Processed(normalizedEvent);
    }

    private NormalizationResult Invalid(LogicalRawEvent rawEvent, NormalizationIssue issue)
    {
        return NormalizationResult.Invalid(rawEvent.RawItemIndex, Version, issue);
    }
}
