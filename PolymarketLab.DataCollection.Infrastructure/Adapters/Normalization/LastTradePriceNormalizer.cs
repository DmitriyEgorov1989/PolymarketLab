using PolymarketLab.DataCollection.Core.Application.Normalization;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;

internal sealed class LastTradePriceNormalizer : IRawMessageNormalizer
{
    public string EventType => "last_trade_price";
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

        var priceResult = PolymarketJsonReader.ReadRequiredDecimal(
            rawEvent.Json,
            PolymarketJsonFields.Price);
        if (priceResult.IsFailure)
            return Invalid(rawEvent, priceResult.Error);

        if (priceResult.Value is < 0 or > 1)
        {
            return Invalid(
                rawEvent,
                new NormalizationIssue(
                    "normalization.field.range.invalid",
                    "Trade price must be between zero and one.",
                    PolymarketJsonFields.Price));
        }

        var sizeResult = PolymarketJsonReader.ReadOptionalDecimal(
            rawEvent.Json,
            PolymarketJsonFields.Size);
        if (sizeResult.IsFailure)
            return Invalid(rawEvent, sizeResult.Error);

        if (sizeResult.Value < 0)
        {
            return Invalid(
                rawEvent,
                new NormalizationIssue(
                    "normalization.field.range.invalid",
                    "Trade size cannot be negative.",
                    PolymarketJsonFields.Size));
        }

        var sideResult = PolymarketJsonReader.ReadRequiredTradeSide(
            rawEvent.Json,
            PolymarketJsonFields.Side);
        if (sideResult.IsFailure)
            return Invalid(rawEvent, sideResult.Error);

        var timestampResult = PolymarketJsonReader.ReadOptionalEpochMilliseconds(
            rawEvent.Json,
            PolymarketJsonFields.Timestamp);
        if (timestampResult.IsFailure)
            return Invalid(rawEvent, timestampResult.Error);

        var feeRateResult = PolymarketJsonReader.ReadOptionalDecimal(
            rawEvent.Json,
            PolymarketJsonFields.FeeRateBps);
        if (feeRateResult.IsFailure)
            return Invalid(rawEvent, feeRateResult.Error);

        var transactionHashResult = PolymarketJsonReader.ReadOptionalString(
            rawEvent.Json,
            PolymarketJsonFields.TransactionHash);
        if (transactionHashResult.IsFailure)
            return Invalid(rawEvent, transactionHashResult.Error);

        var record = new LastTradeRecord(
            priceResult.Value,
            sizeResult.Value,
            sideResult.Value,
            feeRateResult.Value,
            transactionHashResult.Value);
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
            records: [record]);

        return NormalizationResult.Processed(normalizedEvent);
    }

    private NormalizationResult Invalid(
        LogicalRawEvent rawEvent,
        NormalizationIssue issue)
    {
        return NormalizationResult.Invalid(rawEvent.RawItemIndex, Version, issue);
    }
}
