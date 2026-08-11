using System.Text.Json;
using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Application.Normalization;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;

internal sealed class BookNormalizer : IRawMessageNormalizer
{
    public string EventType => "book";
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

        var hashResult = PolymarketJsonReader.ReadRequiredString(
            rawEvent.Json,
            PolymarketJsonFields.Hash);
        if (hashResult.IsFailure)
            return Invalid(rawEvent, hashResult.Error);

        var timestampResult = PolymarketJsonReader.ReadOptionalEpochMilliseconds(
            rawEvent.Json,
            PolymarketJsonFields.Timestamp);
        if (timestampResult.IsFailure)
            return Invalid(rawEvent, timestampResult.Error);

        var tickSizeResult = PolymarketJsonReader.ReadOptionalDecimal(
            rawEvent.Json,
            PolymarketJsonFields.TickSize);
        if (tickSizeResult.IsFailure)
            return Invalid(rawEvent, tickSizeResult.Error);

        if (tickSizeResult.Value <= 0)
        {
            return Invalid(
                rawEvent,
                new NormalizationIssue(
                    "normalization.field.range.invalid",
                    "Tick size must be positive.",
                    PolymarketJsonFields.TickSize));
        }

        var lastTradePriceResult = PolymarketJsonReader.ReadOptionalDecimal(
            rawEvent.Json,
            PolymarketJsonFields.LastTradePrice);
        if (lastTradePriceResult.IsFailure)
            return Invalid(rawEvent, lastTradePriceResult.Error);

        if (lastTradePriceResult.Value is < 0 or > 1)
        {
            return Invalid(
                rawEvent,
                new NormalizationIssue(
                    "normalization.field.range.invalid",
                    "Last trade price must be between zero and one.",
                    PolymarketJsonFields.LastTradePrice));
        }

        var bidsResult = NormalizeLevels(
            rawEvent.Json,
            PolymarketJsonFields.Bids,
            OrderBookSide.Bid);
        if (bidsResult.IsFailure)
            return Invalid(rawEvent, bidsResult.Error);

        var asksResult = NormalizeLevels(
            rawEvent.Json,
            PolymarketJsonFields.Asks,
            OrderBookSide.Ask);
        if (asksResult.IsFailure)
            return Invalid(rawEvent, asksResult.Error);

        var records = new List<NormalizedRecord>(
            1 + bidsResult.Value.Count + asksResult.Value.Count)
        {
            new BookSnapshotRecord(
                hashResult.Value,
                tickSizeResult.Value,
                lastTradePriceResult.Value)
        };
        records.AddRange(bidsResult.Value);
        records.AddRange(asksResult.Value);

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
            records: records);

        return NormalizationResult.Processed(normalizedEvent);
    }

    private static Result<List<BookLevelRecord>, NormalizationIssue> NormalizeLevels(
        JsonElement json,
        string field,
        OrderBookSide side)
    {
        if (!json.TryGetProperty(field, out var levels) || levels.ValueKind == JsonValueKind.Null)
        {
            return new NormalizationIssue(
                "normalization.field.required",
                "Required field is missing or empty.",
                field);
        }

        if (levels.ValueKind != JsonValueKind.Array)
        {
            return new NormalizationIssue(
                "normalization.field.array.invalid",
                "Field must be a JSON array.",
                field);
        }

        var records = new List<BookLevelRecord>(levels.GetArrayLength());
        var levelIndex = 0;

        foreach (var level in levels.EnumerateArray())
        {
            if (level.ValueKind != JsonValueKind.Object)
            {
                return new NormalizationIssue(
                    "normalization.field.object.invalid",
                    "Array item must be a JSON object.",
                    LevelPath(field, levelIndex));
            }

            var priceResult = PolymarketJsonReader.ReadRequiredDecimal(
                level,
                PolymarketJsonFields.Price);
            if (priceResult.IsFailure)
                return AtLevel(field, levelIndex, priceResult.Error);

            if (priceResult.Value is < 0 or > 1)
            {
                return AtLevel(
                    field,
                    levelIndex,
                    new NormalizationIssue(
                        "normalization.field.range.invalid",
                        "Book level price must be between zero and one.",
                        PolymarketJsonFields.Price));
            }

            var sizeResult = PolymarketJsonReader.ReadRequiredDecimal(
                level,
                PolymarketJsonFields.Size);
            if (sizeResult.IsFailure)
                return AtLevel(field, levelIndex, sizeResult.Error);

            if (sizeResult.Value < 0)
            {
                return AtLevel(
                    field,
                    levelIndex,
                    new NormalizationIssue(
                        "normalization.field.range.invalid",
                        "Book level size cannot be negative.",
                        PolymarketJsonFields.Size));
            }

            records.Add(new BookLevelRecord(
                side,
                levelIndex,
                priceResult.Value,
                sizeResult.Value));
            levelIndex++;
        }

        return records;
    }

    private static NormalizationIssue AtLevel(
        string field,
        int levelIndex,
        NormalizationIssue issue)
    {
        var path = issue.Field is null
            ? LevelPath(field, levelIndex)
            : $"{LevelPath(field, levelIndex)}.{issue.Field}";

        return new NormalizationIssue(issue.Code, issue.Message, path);
    }

    private static string LevelPath(string field, int levelIndex)
    {
        return $"{field}[{levelIndex}]";
    }

    private NormalizationResult Invalid(
        LogicalRawEvent rawEvent,
        NormalizationIssue issue)
    {
        return NormalizationResult.Invalid(rawEvent.RawItemIndex, Version, issue);
    }
}
