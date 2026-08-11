using System.Text.Json;
using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Application.Normalization;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;

internal sealed class NewMarketNormalizer : IRawMessageNormalizer
{
    public string EventType => "new_market";
    public int Version => 1;

    public NormalizationResult Normalize(LogicalRawEvent rawEvent)
    {
        ArgumentNullException.ThrowIfNull(rawEvent);

        var market = PolymarketJsonReader.ReadRequiredString(rawEvent.Json, PolymarketJsonFields.Market);
        if (market.IsFailure) return Invalid(rawEvent, market.Error);
        var timestamp = PolymarketJsonReader.ReadOptionalEpochMilliseconds(rawEvent.Json, PolymarketJsonFields.Timestamp);
        if (timestamp.IsFailure) return Invalid(rawEvent, timestamp.Error);
        var externalId = PolymarketJsonReader.ReadRequiredString(rawEvent.Json, PolymarketJsonFields.Id);
        if (externalId.IsFailure) return Invalid(rawEvent, externalId.Error);
        var question = PolymarketJsonReader.ReadRequiredString(rawEvent.Json, PolymarketJsonFields.Question);
        if (question.IsFailure) return Invalid(rawEvent, question.Error);
        var slug = PolymarketJsonReader.ReadRequiredString(rawEvent.Json, PolymarketJsonFields.Slug);
        if (slug.IsFailure) return Invalid(rawEvent, slug.Error);
        var description = PolymarketJsonReader.ReadRequiredString(rawEvent.Json, PolymarketJsonFields.Description);
        if (description.IsFailure) return Invalid(rawEvent, description.Error);
        var active = PolymarketJsonReader.ReadRequiredBoolean(rawEvent.Json, PolymarketJsonFields.Active);
        if (active.IsFailure) return Invalid(rawEvent, active.Error);
        var sportsMarketType = PolymarketJsonReader.ReadRequiredStringAllowingEmpty(rawEvent.Json, PolymarketJsonFields.SportsMarketType);
        if (sportsMarketType.IsFailure) return Invalid(rawEvent, sportsMarketType.Error);
        var line = PolymarketJsonReader.ReadRequiredDecimalAllowingEmpty(rawEvent.Json, PolymarketJsonFields.Line);
        if (line.IsFailure) return Invalid(rawEvent, line.Error);
        var gameStartTime = PolymarketJsonReader.ReadRequiredStringAllowingEmpty(rawEvent.Json, PolymarketJsonFields.GameStartTime);
        if (gameStartTime.IsFailure) return Invalid(rawEvent, gameStartTime.Error);
        var tickSize = PolymarketJsonReader.ReadRequiredDecimal(rawEvent.Json, PolymarketJsonFields.OrderPriceMinTickSize);
        if (tickSize.IsFailure) return Invalid(rawEvent, tickSize.Error);
        var groupItemTitle = PolymarketJsonReader.ReadRequiredStringAllowingEmpty(rawEvent.Json, PolymarketJsonFields.GroupItemTitle);
        if (groupItemTitle.IsFailure) return Invalid(rawEvent, groupItemTitle.Error);
        var takerBaseFee = PolymarketJsonReader.ReadRequiredDecimal(rawEvent.Json, PolymarketJsonFields.TakerBaseFee);
        if (takerBaseFee.IsFailure) return Invalid(rawEvent, takerBaseFee.Error);
        var feesEnabled = PolymarketJsonReader.ReadRequiredBoolean(rawEvent.Json, PolymarketJsonFields.FeesEnabled);
        if (feesEnabled.IsFailure) return Invalid(rawEvent, feesEnabled.Error);

        var eventMessage = ReadEventMessage(rawEvent.Json);
        if (eventMessage.IsFailure) return Invalid(rawEvent, eventMessage.Error);
        var feeSchedule = ReadFeeSchedule(rawEvent.Json);
        if (feeSchedule.IsFailure) return Invalid(rawEvent, feeSchedule.Error);
        var assets = ReadRequiredStringArray(rawEvent.Json, PolymarketJsonFields.AssetsIds);
        if (assets.IsFailure) return Invalid(rawEvent, assets.Error);
        var outcomes = ReadRequiredStringArray(rawEvent.Json, PolymarketJsonFields.Outcomes);
        if (outcomes.IsFailure) return Invalid(rawEvent, outcomes.Error);

        if (assets.Value.Count != outcomes.Value.Count)
        {
            return Invalid(rawEvent, new NormalizationIssue(
                "normalization.field.array.length_mismatch",
                "Outcomes must contain the same number of items as asset ids.",
                PolymarketJsonFields.Outcomes));
        }

        var marketRecord = new NewMarketRecord(
            externalId.Value,
            question.Value,
            slug.Value,
            description.Value,
            active.Value,
            sportsMarketType.Value,
            line.Value,
            gameStartTime.Value,
            tickSize.Value,
            groupItemTitle.Value,
            takerBaseFee.Value,
            feesEnabled.Value,
            eventMessage.Value,
            feeSchedule.Value);
        var records = new List<NormalizedRecord>(assets.Value.Count + 1) { marketRecord };
        for (var index = 0; index < assets.Value.Count; index++)
            records.Add(new NewMarketAssetRecord(index, assets.Value[index], outcomes.Value[index]));

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

    private static Result<NewMarketEventMessage, NormalizationIssue> ReadEventMessage(JsonElement json)
    {
        var objectResult = ReadRequiredObject(json, PolymarketJsonFields.EventMessage);
        if (objectResult.IsFailure) return objectResult.Error;

        var id = PolymarketJsonReader.ReadRequiredString(objectResult.Value, PolymarketJsonFields.Id);
        if (id.IsFailure) return AtObject(PolymarketJsonFields.EventMessage, id.Error);
        var ticker = PolymarketJsonReader.ReadRequiredString(objectResult.Value, PolymarketJsonFields.Ticker);
        if (ticker.IsFailure) return AtObject(PolymarketJsonFields.EventMessage, ticker.Error);
        var slug = PolymarketJsonReader.ReadRequiredString(objectResult.Value, PolymarketJsonFields.Slug);
        if (slug.IsFailure) return AtObject(PolymarketJsonFields.EventMessage, slug.Error);
        var title = PolymarketJsonReader.ReadRequiredString(objectResult.Value, PolymarketJsonFields.Title);
        if (title.IsFailure) return AtObject(PolymarketJsonFields.EventMessage, title.Error);
        var description = PolymarketJsonReader.ReadRequiredString(objectResult.Value, PolymarketJsonFields.Description);
        if (description.IsFailure) return AtObject(PolymarketJsonFields.EventMessage, description.Error);

        return new NewMarketEventMessage(id.Value, ticker.Value, slug.Value, title.Value, description.Value);
    }

    private static Result<NewMarketFeeSchedule, NormalizationIssue> ReadFeeSchedule(JsonElement json)
    {
        var objectResult = ReadRequiredObject(json, PolymarketJsonFields.FeeSchedule);
        if (objectResult.IsFailure) return objectResult.Error;

        var exponent = PolymarketJsonReader.ReadRequiredDecimal(objectResult.Value, PolymarketJsonFields.Exponent);
        if (exponent.IsFailure) return AtObject(PolymarketJsonFields.FeeSchedule, exponent.Error);
        var rate = PolymarketJsonReader.ReadRequiredDecimal(objectResult.Value, PolymarketJsonFields.Rate);
        if (rate.IsFailure) return AtObject(PolymarketJsonFields.FeeSchedule, rate.Error);
        var rebateRate = PolymarketJsonReader.ReadRequiredDecimal(objectResult.Value, PolymarketJsonFields.RebateRate);
        if (rebateRate.IsFailure) return AtObject(PolymarketJsonFields.FeeSchedule, rebateRate.Error);
        var takerOnly = PolymarketJsonReader.ReadRequiredBoolean(objectResult.Value, PolymarketJsonFields.TakerOnly);
        if (takerOnly.IsFailure) return AtObject(PolymarketJsonFields.FeeSchedule, takerOnly.Error);

        return new NewMarketFeeSchedule(exponent.Value, rate.Value, rebateRate.Value, takerOnly.Value);
    }

    private static Result<JsonElement, NormalizationIssue> ReadRequiredObject(JsonElement json, string field)
    {
        if (!json.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null)
            return new NormalizationIssue("normalization.field.required", "Required field is missing or empty.", field);
        if (value.ValueKind != JsonValueKind.Object)
            return new NormalizationIssue("normalization.field.object.invalid", "Field must be a JSON object.", field);
        return value;
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

    private static NormalizationIssue AtObject(string objectField, NormalizationIssue issue)
    {
        return new NormalizationIssue(issue.Code, issue.Message, $"{objectField}.{issue.Field}");
    }

    private NormalizationResult Invalid(LogicalRawEvent rawEvent, NormalizationIssue issue)
    {
        return NormalizationResult.Invalid(rawEvent.RawItemIndex, Version, issue);
    }
}
