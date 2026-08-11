using System.Text.Json;
using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Application.Normalization;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;

internal sealed class PriceChangeNormalizer : IRawMessageNormalizer
{
    public string EventType => "price_change";
    public int Version => 1;

    public NormalizationResult Normalize(LogicalRawEvent rawEvent)
    {
        ArgumentNullException.ThrowIfNull(rawEvent);

        var marketResult = PolymarketJsonReader.ReadRequiredString(
            rawEvent.Json,
            PolymarketJsonFields.Market);
        if (marketResult.IsFailure)
            return Invalid(rawEvent, marketResult.Error);

        var timestampResult = PolymarketJsonReader.ReadOptionalEpochMilliseconds(
            rawEvent.Json,
            PolymarketJsonFields.Timestamp);
        if (timestampResult.IsFailure)
            return Invalid(rawEvent, timestampResult.Error);

        if (!rawEvent.Json.TryGetProperty(
                PolymarketJsonFields.PriceChanges,
                out var priceChanges) ||
            priceChanges.ValueKind == JsonValueKind.Null)
        {
            return Invalid(
                rawEvent,
                new NormalizationIssue(
                    "normalization.field.required",
                    "Required field is missing or empty.",
                    PolymarketJsonFields.PriceChanges));
        }

        if (priceChanges.ValueKind != JsonValueKind.Array)
        {
            return Invalid(
                rawEvent,
                new NormalizationIssue(
                    "normalization.field.array.invalid",
                    "Field must be a JSON array.",
                    PolymarketJsonFields.PriceChanges));
        }

        if (priceChanges.GetArrayLength() == 0)
        {
            return Invalid(
                rawEvent,
                new NormalizationIssue(
                    "normalization.field.array.empty",
                    "Field must contain at least one item.",
                    PolymarketJsonFields.PriceChanges));
        }

        var records = new List<PriceChangeRecord>(priceChanges.GetArrayLength());
        var itemIndex = 0;

        foreach (var item in priceChanges.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return Invalid(
                    rawEvent,
                    new NormalizationIssue(
                        "normalization.field.object.invalid",
                        "Array item must be a JSON object.",
                        ItemPath(itemIndex)));
            }

            var recordResult = NormalizeItem(item, itemIndex);
            if (recordResult.IsFailure)
                return Invalid(rawEvent, recordResult.Error);

            records.Add(recordResult.Value);
            itemIndex++;
        }

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
            assetId: null,
            records: records);

        return NormalizationResult.Processed(normalizedEvent);
    }

    private static Result<PriceChangeRecord, NormalizationIssue> NormalizeItem(
        JsonElement item,
        int itemIndex)
    {
        var assetIdResult = PolymarketJsonReader.ReadRequiredString(
            item,
            PolymarketJsonFields.AssetId);
        if (assetIdResult.IsFailure)
            return AtItem(itemIndex, assetIdResult.Error);

        var priceResult = PolymarketJsonReader.ReadRequiredDecimal(
            item,
            PolymarketJsonFields.Price);
        if (priceResult.IsFailure)
            return AtItem(itemIndex, priceResult.Error);

        if (priceResult.Value is < 0 or > 1)
        {
            return AtItem(
                itemIndex,
                new NormalizationIssue(
                    "normalization.field.range.invalid",
                    "Price must be between zero and one.",
                    PolymarketJsonFields.Price));
        }

        var sizeResult = PolymarketJsonReader.ReadRequiredDecimal(
            item,
            PolymarketJsonFields.Size);
        if (sizeResult.IsFailure)
            return AtItem(itemIndex, sizeResult.Error);

        if (sizeResult.Value < 0)
        {
            return AtItem(
                itemIndex,
                new NormalizationIssue(
                    "normalization.field.range.invalid",
                    "Size cannot be negative.",
                    PolymarketJsonFields.Size));
        }

        var sideResult = PolymarketJsonReader.ReadRequiredTradeSide(
            item,
            PolymarketJsonFields.Side);
        if (sideResult.IsFailure)
            return AtItem(itemIndex, sideResult.Error);

        var hashResult = PolymarketJsonReader.ReadOptionalString(
            item,
            PolymarketJsonFields.Hash);
        if (hashResult.IsFailure)
            return AtItem(itemIndex, hashResult.Error);

        var bestBidResult = PolymarketJsonReader.ReadOptionalDecimal(
            item,
            PolymarketJsonFields.BestBid);
        if (bestBidResult.IsFailure)
            return AtItem(itemIndex, bestBidResult.Error);

        if (bestBidResult.Value is < 0 or > 1)
        {
            return AtItem(
                itemIndex,
                new NormalizationIssue(
                    "normalization.field.range.invalid",
                    "Best bid must be between zero and one.",
                    PolymarketJsonFields.BestBid));
        }

        var bestAskResult = PolymarketJsonReader.ReadOptionalDecimal(
            item,
            PolymarketJsonFields.BestAsk);
        if (bestAskResult.IsFailure)
            return AtItem(itemIndex, bestAskResult.Error);

        if (bestAskResult.Value is < 0 or > 1)
        {
            return AtItem(
                itemIndex,
                new NormalizationIssue(
                    "normalization.field.range.invalid",
                    "Best ask must be between zero and one.",
                    PolymarketJsonFields.BestAsk));
        }

        return new PriceChangeRecord(
            itemIndex,
            assetIdResult.Value,
            priceResult.Value,
            sizeResult.Value,
            sideResult.Value,
            hashResult.Value,
            bestBidResult.Value,
            bestAskResult.Value);
    }

    private static NormalizationIssue AtItem(
        int itemIndex,
        NormalizationIssue issue)
    {
        var field = issue.Field is null
            ? ItemPath(itemIndex)
            : $"{ItemPath(itemIndex)}.{issue.Field}";

        return new NormalizationIssue(issue.Code, issue.Message, field);
    }

    private static string ItemPath(int itemIndex)
    {
        return $"{PolymarketJsonFields.PriceChanges}[{itemIndex}]";
    }

    private NormalizationResult Invalid(
        LogicalRawEvent rawEvent,
        NormalizationIssue issue)
    {
        return NormalizationResult.Invalid(rawEvent.RawItemIndex, Version, issue);
    }
}
