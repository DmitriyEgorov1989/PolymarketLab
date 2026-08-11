using PolymarketLab.DataCollection.Core.Application.Normalization;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;

internal sealed class BestBidAskNormalizer : IRawMessageNormalizer
{
    public string EventType => "best_bid_ask";
    public int Version => 1;

    public NormalizationResult Normalize(LogicalRawEvent rawEvent)
    {
        ArgumentNullException.ThrowIfNull(rawEvent);

        var marketResult = PolymarketJsonReader.ReadRequiredString(rawEvent.Json, PolymarketJsonFields.Market);
        if (marketResult.IsFailure)
            return Invalid(rawEvent, marketResult.Error);

        var assetIdResult = PolymarketJsonReader.ReadRequiredString(rawEvent.Json, PolymarketJsonFields.AssetId);
        if (assetIdResult.IsFailure)
            return Invalid(rawEvent, assetIdResult.Error);

        var bestBidResult = PolymarketJsonReader.ReadRequiredDecimal(rawEvent.Json, PolymarketJsonFields.BestBid);
        if (bestBidResult.IsFailure)
            return Invalid(rawEvent, bestBidResult.Error);
        if (bestBidResult.Value is < 0 or > 1)
            return RangeInvalid(rawEvent, PolymarketJsonFields.BestBid, "Best bid must be between zero and one.");

        var bestAskResult = PolymarketJsonReader.ReadRequiredDecimal(rawEvent.Json, PolymarketJsonFields.BestAsk);
        if (bestAskResult.IsFailure)
            return Invalid(rawEvent, bestAskResult.Error);
        if (bestAskResult.Value is < 0 or > 1)
            return RangeInvalid(rawEvent, PolymarketJsonFields.BestAsk, "Best ask must be between zero and one.");

        var spreadResult = PolymarketJsonReader.ReadRequiredDecimal(rawEvent.Json, PolymarketJsonFields.Spread);
        if (spreadResult.IsFailure)
            return Invalid(rawEvent, spreadResult.Error);
        if (spreadResult.Value < 0)
            return RangeInvalid(rawEvent, PolymarketJsonFields.Spread, "Spread cannot be negative.");

        var timestampResult = PolymarketJsonReader.ReadOptionalEpochMilliseconds(rawEvent.Json, PolymarketJsonFields.Timestamp);
        if (timestampResult.IsFailure)
            return Invalid(rawEvent, timestampResult.Error);

        var normalizedEvent = new NormalizedEvent(
            rawEvent.RawMessageId,
            rawEvent.RawItemIndex,
            rawEvent.ProjectionVersion,
            Version,
            EventType,
            rawEvent.SessionId,
            rawEvent.ReceivedAt,
            timestampResult.Value,
            marketResult.Value,
            assetIdResult.Value,
            [new BestBidAskRecord(bestBidResult.Value, bestAskResult.Value, spreadResult.Value)]);

        return NormalizationResult.Processed(normalizedEvent);
    }

    private NormalizationResult RangeInvalid(LogicalRawEvent rawEvent, string field, string message)
    {
        return Invalid(rawEvent, new NormalizationIssue("normalization.field.range.invalid", message, field));
    }

    private NormalizationResult Invalid(LogicalRawEvent rawEvent, NormalizationIssue issue)
    {
        return NormalizationResult.Invalid(rawEvent.RawItemIndex, Version, issue);
    }
}
