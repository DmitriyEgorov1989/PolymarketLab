using System.Text.Json;
using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Application.Normalization;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;

internal sealed class MarketResolvedNormalizer : IRawMessageNormalizer
{
    public string EventType => "market_resolved";
    public int Version => 1;

    public NormalizationResult Normalize(LogicalRawEvent rawEvent)
    {
        ArgumentNullException.ThrowIfNull(rawEvent);

        var externalMarketId = PolymarketJsonReader.ReadRequiredString(rawEvent.Json, PolymarketJsonFields.Id);
        if (externalMarketId.IsFailure) return Invalid(rawEvent, externalMarketId.Error);
        var market = PolymarketJsonReader.ReadRequiredString(rawEvent.Json, PolymarketJsonFields.Market);
        if (market.IsFailure) return Invalid(rawEvent, market.Error);
        var winningAssetId = PolymarketJsonReader.ReadRequiredString(rawEvent.Json, PolymarketJsonFields.WinningAssetId);
        if (winningAssetId.IsFailure) return Invalid(rawEvent, winningAssetId.Error);
        var winningOutcome = PolymarketJsonReader.ReadRequiredString(rawEvent.Json, PolymarketJsonFields.WinningOutcome);
        if (winningOutcome.IsFailure) return Invalid(rawEvent, winningOutcome.Error);
        var timestamp = PolymarketJsonReader.ReadOptionalEpochMilliseconds(rawEvent.Json, PolymarketJsonFields.Timestamp);
        if (timestamp.IsFailure) return Invalid(rawEvent, timestamp.Error);
        var assets = ReadRequiredStringArray(rawEvent.Json, PolymarketJsonFields.AssetsIds);
        if (assets.IsFailure) return Invalid(rawEvent, assets.Error);

        var records = new List<NormalizedRecord>(assets.Value.Count + 1)
        {
            new MarketResolvedRecord(externalMarketId.Value, winningAssetId.Value, winningOutcome.Value)
        };
        for (var index = 0; index < assets.Value.Count; index++)
            records.Add(new MarketResolvedAssetRecord(index, assets.Value[index]));

        return NormalizationResult.Processed(new NormalizedEvent(
            rawEvent.RawMessageId,
            rawEvent.RawItemIndex,
            rawEvent.ProjectionVersion,
            Version,
            EventType,
            rawEvent.SessionId,
            rawEvent.ReceivedAt,
            timestamp.Value,
            market.Value,
            assetId: null,
            records));
    }

    private static Result<IReadOnlyList<string>, NormalizationIssue> ReadRequiredStringArray(
        JsonElement json,
        string field)
    {
        if (!json.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null)
            return new NormalizationIssue("normalization.field.required", "Required field is missing or empty.", field);
        if (value.ValueKind != JsonValueKind.Array)
            return new NormalizationIssue("normalization.field.array.invalid", "Field must be a JSON array.", field);
        if (value.GetArrayLength() == 0)
            return new NormalizationIssue("normalization.field.array.empty", "Field must contain at least one item.", field);

        var items = new List<string>(value.GetArrayLength());
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            var itemPath = $"{field}[{index}]";
            if (item.ValueKind != JsonValueKind.String)
                return new NormalizationIssue("normalization.field.string.invalid", "Field must be a JSON string.", itemPath);
            var text = item.GetString();
            if (string.IsNullOrWhiteSpace(text))
                return new NormalizationIssue("normalization.field.required", "Required field is missing or empty.", itemPath);
            items.Add(text);
            index++;
        }

        return Result.Success<IReadOnlyList<string>, NormalizationIssue>(items);
    }

    private NormalizationResult Invalid(LogicalRawEvent rawEvent, NormalizationIssue issue)
    {
        return NormalizationResult.Invalid(rawEvent.RawItemIndex, Version, issue);
    }
}
