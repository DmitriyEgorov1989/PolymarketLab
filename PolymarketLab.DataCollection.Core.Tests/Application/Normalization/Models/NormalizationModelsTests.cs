using System.Text.Json;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.Normalization.Models;

public sealed class NormalizationModelsTests
{
    [Fact]
    public void RawMessageEnvelope_ShouldOwnPayloadCopy()
    {
        var source = new byte[] { 1, 2, 3 };

        var envelope = new RawMessageEnvelope(
            42,
            CreateSessionId(),
            DateTimeOffset.Parse("2026-08-10T10:00:00Z"),
            source);
        source[0] = 9;

        envelope.Payload.ToArray().Should().Equal(1, 2, 3);
    }

    [Fact]
    public void LogicalRawEvent_ShouldPreserveIdentityAndOwnJsonClone()
    {
        LogicalRawEvent rawEvent;
        using (var document = JsonDocument.Parse("""{"event_type":"book"}"""))
        {
            rawEvent = new LogicalRawEvent(
                rawMessageId: 42,
                rawItemIndex: 1,
                projectionVersion: 2,
                sessionId: CreateSessionId(),
                receivedAt: DateTimeOffset.Parse("2026-08-10T10:00:00Z"),
                json: document.RootElement);
        }

        rawEvent.RawMessageId.Should().Be(42);
        rawEvent.RawItemIndex.Should().Be(1);
        rawEvent.ProjectionVersion.Should().Be(2);
        rawEvent.SessionId.Should().Be(CreateSessionId());
        rawEvent.ReceivedAt.Should().Be(DateTimeOffset.Parse("2026-08-10T10:00:00Z"));
        rawEvent.Json.GetProperty("event_type").GetString().Should().Be("book");
    }

    [Fact]
    public void NormalizedEvent_ShouldPreserveIdentityAndOwnRecordsCopy()
    {
        var records = new List<NormalizedRecord>
        {
            new StubNormalizedRecord("first")
        };

        var normalizedEvent = CreateEvent(records);
        records.Clear();

        normalizedEvent.RawMessageId.Should().Be(42);
        normalizedEvent.RawItemIndex.Should().Be(1);
        normalizedEvent.ProjectionVersion.Should().Be(2);
        normalizedEvent.NormalizerVersion.Should().Be(3);
        normalizedEvent.EventType.Should().Be("book");
        normalizedEvent.Records.Should().ContainSingle()
            .Which.Should().Be(new StubNormalizedRecord("first"));
    }

    [Fact]
    public void Processed_ShouldContainNormalizedEventWithoutIssue()
    {
        var normalizedEvent = CreateEvent();

        var result = NormalizationResult.Processed(normalizedEvent);

        result.RawItemIndex.Should().Be(1);
        result.Outcome.Should().Be(NormalizationOutcome.Processed);
        result.NormalizerVersion.Should().Be(3);
        result.Event.Should().BeSameAs(normalizedEvent);
        result.Issue.Should().BeNull();
    }

    [Fact]
    public void Invalid_ShouldContainNormalizerVersionAndIssue()
    {
        var issue = new NormalizationIssue(
            "normalization.price.invalid",
            "Price must be a decimal value.",
            "price");

        var result = NormalizationResult.Invalid(2, 4, issue);

        result.RawItemIndex.Should().Be(2);
        result.Outcome.Should().Be(NormalizationOutcome.Invalid);
        result.NormalizerVersion.Should().Be(4);
        result.Event.Should().BeNull();
        result.Issue.Should().BeSameAs(issue);
    }

    [Fact]
    public void InvalidWithoutSelectedNormalizer_ShouldNotAssignNormalizerVersion()
    {
        var issue = new NormalizationIssue(
            "normalization.event_type.required",
            "Event type is required.",
            "event_type");

        var result = NormalizationResult.Invalid(2, issue);

        result.RawItemIndex.Should().Be(2);
        result.Outcome.Should().Be(NormalizationOutcome.Invalid);
        result.NormalizerVersion.Should().BeNull();
        result.Event.Should().BeNull();
        result.Issue.Should().BeSameAs(issue);
    }

    [Fact]
    public void Unsupported_ShouldNotAssignNormalizerVersion()
    {
        var issue = new NormalizationIssue(
            "normalization.event_type.unsupported",
            "Event type is not supported.",
            "event_type");

        var result = NormalizationResult.Unsupported(3, issue);

        result.RawItemIndex.Should().Be(3);
        result.Outcome.Should().Be(NormalizationOutcome.Unsupported);
        result.NormalizerVersion.Should().BeNull();
        result.Event.Should().BeNull();
        result.Issue.Should().BeSameAs(issue);
    }

    [Fact]
    public void NormalizedEvent_WithoutRecords_ShouldRejectInvalidState()
    {
        var action = () => CreateEvent([]);

        action.Should().Throw<ArgumentException>()
            .WithParameterName("records");
    }

    [Fact]
    public void LastTradeRecord_ValidValues_ShouldPreserveOptionalContract()
    {
        var record = new LastTradeRecord(
            price: 0.3m,
            size: null,
            side: TradeSide.Buy,
            feeRateBps: 0m,
            transactionHash: "");

        record.Price.Should().Be(0.3m);
        record.Size.Should().BeNull();
        record.Side.Should().Be(TradeSide.Buy);
        record.FeeRateBps.Should().Be(0m);
        record.TransactionHash.Should().BeEmpty();
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1.01")]
    public void LastTradeRecord_PriceOutsideRange_ShouldRejectInvalidState(string price)
    {
        var action = () => new LastTradeRecord(
            decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture),
            size: 1m,
            side: TradeSide.Buy,
            feeRateBps: null,
            transactionHash: null);

        action.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("price");
    }

    [Fact]
    public void LastTradeRecord_NegativeSize_ShouldRejectInvalidState()
    {
        var action = () => new LastTradeRecord(
            price: 0.5m,
            size: -0.01m,
            side: TradeSide.Sell,
            feeRateBps: null,
            transactionHash: null);

        action.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("size");
    }

    [Fact]
    public void PriceChangeRecord_ValidValues_ShouldPreserveItemIdentityAndOptionalFields()
    {
        var record = new PriceChangeRecord(
            itemIndex: 1,
            assetId: "asset",
            price: 0.9m,
            size: 0m,
            side: TradeSide.Sell,
            hash: "",
            bestBid: null,
            bestAsk: 1m);

        record.ItemIndex.Should().Be(1);
        record.AssetId.Should().Be("asset");
        record.Price.Should().Be(0.9m);
        record.Size.Should().Be(0m);
        record.Side.Should().Be(TradeSide.Sell);
        record.Hash.Should().BeEmpty();
        record.BestBid.Should().BeNull();
        record.BestAsk.Should().Be(1m);
    }

    [Theory]
    [InlineData(-1, "asset", "0.5", "1", null, null, "itemIndex")]
    [InlineData(0, "", "0.5", "1", null, null, "assetId")]
    [InlineData(0, "asset", "-0.01", "1", null, null, "price")]
    [InlineData(0, "asset", "1.01", "1", null, null, "price")]
    [InlineData(0, "asset", "0.5", "-0.01", null, null, "size")]
    [InlineData(0, "asset", "0.5", "1", "-0.01", null, "bestBid")]
    [InlineData(0, "asset", "0.5", "1", null, "1.01", "bestAsk")]
    public void PriceChangeRecord_InvalidInvariant_ShouldRejectInvalidState(
        int itemIndex,
        string assetId,
        string price,
        string size,
        string? bestBid,
        string? bestAsk,
        string expectedParameter)
    {
        var action = () => new PriceChangeRecord(
            itemIndex,
            assetId,
            decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(size, System.Globalization.CultureInfo.InvariantCulture),
            TradeSide.Buy,
            hash: null,
            bestBid is null
                ? null
                : decimal.Parse(bestBid, System.Globalization.CultureInfo.InvariantCulture),
            bestAsk is null
                ? null
                : decimal.Parse(bestAsk, System.Globalization.CultureInfo.InvariantCulture));

        action.Should().Throw<ArgumentException>()
            .WithParameterName(expectedParameter);
    }

    [Fact]
    public void OrderBookSide_ShouldHaveStablePersistenceValues()
    {
        ((int)OrderBookSide.Bid).Should().Be(1);
        ((int)OrderBookSide.Ask).Should().Be(2);
    }

    [Fact]
    public void TradeSide_ShouldHaveStablePersistenceValues()
    {
        ((int)TradeSide.Buy).Should().Be(1);
        ((int)TradeSide.Sell).Should().Be(2);
    }

    [Fact]
    public void BookSnapshotRecord_ValidValues_ShouldPreserveConfirmedContract()
    {
        var record = new BookSnapshotRecord(
            hash: "snapshot-hash",
            tickSize: 0.01m,
            lastTradePrice: 0.7m);

        record.Hash.Should().Be("snapshot-hash");
        record.TickSize.Should().Be(0.01m);
        record.LastTradePrice.Should().Be(0.7m);
    }

    [Fact]
    public void BookSnapshotRecord_MissingOptionalValues_ShouldPreserveNulls()
    {
        var record = new BookSnapshotRecord("snapshot-hash", null, null);

        record.TickSize.Should().BeNull();
        record.LastTradePrice.Should().BeNull();
    }

    [Theory]
    [InlineData("", "0.01", "0.5", "hash")]
    [InlineData("hash", "0", "0.5", "tickSize")]
    [InlineData("hash", "-0.01", "0.5", "tickSize")]
    [InlineData("hash", "0.01", "-0.01", "lastTradePrice")]
    [InlineData("hash", "0.01", "1.01", "lastTradePrice")]
    public void BookSnapshotRecord_InvalidInvariant_ShouldRejectInvalidState(
        string hash,
        string tickSize,
        string lastTradePrice,
        string expectedParameter)
    {
        var action = () => new BookSnapshotRecord(
            hash,
            decimal.Parse(tickSize, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(lastTradePrice, System.Globalization.CultureInfo.InvariantCulture));

        action.Should().Throw<ArgumentException>()
            .WithParameterName(expectedParameter);
    }

    [Fact]
    public void BookLevelRecord_ValidValues_ShouldPreserveSourcePosition()
    {
        var record = new BookLevelRecord(
            OrderBookSide.Ask,
            levelIndex: 3,
            price: 0.8m,
            size: 12.5m);

        record.Side.Should().Be(OrderBookSide.Ask);
        record.LevelIndex.Should().Be(3);
        record.Price.Should().Be(0.8m);
        record.Size.Should().Be(12.5m);
    }

    [Theory]
    [InlineData(0, -1, "0.5", "1", "side")]
    [InlineData(1, -1, "0.5", "1", "levelIndex")]
    [InlineData(1, 0, "-0.01", "1", "price")]
    [InlineData(1, 0, "1.01", "1", "price")]
    [InlineData(2, 0, "0.5", "-0.01", "size")]
    public void BookLevelRecord_InvalidInvariant_ShouldRejectInvalidState(
        int side,
        int levelIndex,
        string price,
        string size,
        string expectedParameter)
    {
        var action = () => new BookLevelRecord(
            (OrderBookSide)side,
            levelIndex,
            decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(size, System.Globalization.CultureInfo.InvariantCulture));

        action.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(expectedParameter);
    }

    [Fact]
    public void TickSizeChangeRecord_ShouldPreserveDecimalValues()
    {
        var record = new TickSizeChangeRecord(0.01m, 0.001m);

        record.OldTickSize.Should().Be(0.01m);
        record.NewTickSize.Should().Be(0.001m);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.001")]
    public void TickSizeChangeRecord_NonPositiveNewValue_ShouldRejectInvalidState(string value)
    {
        var action = () => new TickSizeChangeRecord(
            0.01m,
            decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture));

        action.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("newTickSize");
    }

    [Fact]
    public void BestBidAskRecord_ShouldPreserveBoundaryValues()
    {
        var record = new BestBidAskRecord(0m, 1m, 1m);

        record.BestBid.Should().Be(0m);
        record.BestAsk.Should().Be(1m);
        record.Spread.Should().Be(1m);
    }

    [Theory]
    [InlineData("-0.001", "1", "1", "bestBid")]
    [InlineData("0", "1.001", "1", "bestAsk")]
    [InlineData("0", "1", "-0.001", "spread")]
    public void BestBidAskRecord_InvalidValue_ShouldRejectInvalidState(
        string bestBid,
        string bestAsk,
        string spread,
        string expectedParameter)
    {
        var action = () => new BestBidAskRecord(
            decimal.Parse(bestBid, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(bestAsk, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(spread, System.Globalization.CultureInfo.InvariantCulture));

        action.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(expectedParameter);
    }

    [Fact]
    public void NewMarketRecords_ShouldPreserveConfirmedContractAndOrderedMapping()
    {
        var eventMessage = new NewMarketEventMessage(
            "event-id", "ticker", "event-slug", "Event title", "Event description");
        var feeSchedule = new NewMarketFeeSchedule(2m, 0.02m, 0.25m, true);
        var market = new NewMarketRecord(
            "market-id",
            "Question?",
            "market-slug",
            "Description",
            true,
            "",
            null,
            "",
            0.01m,
            "",
            0m,
            false,
            eventMessage,
            feeSchedule);
        var asset = new NewMarketAssetRecord(1, "asset-id", "Down");

        market.ExternalId.Should().Be("market-id");
        market.SportsMarketType.Should().BeEmpty();
        market.Line.Should().BeNull();
        market.GameStartTime.Should().BeEmpty();
        market.GroupItemTitle.Should().BeEmpty();
        market.EventMessage.Should().BeSameAs(eventMessage);
        market.FeeSchedule.Should().BeSameAs(feeSchedule);
        asset.ItemIndex.Should().Be(1);
        asset.AssetId.Should().Be("asset-id");
        asset.Outcome.Should().Be("Down");
    }

    [Theory]
    [InlineData(-1, "asset", "Up", "itemIndex")]
    [InlineData(0, "", "Up", "assetId")]
    [InlineData(0, "asset", "", "outcome")]
    public void NewMarketAssetRecord_InvalidMapping_ShouldRejectInvalidState(
        int itemIndex,
        string assetId,
        string outcome,
        string expectedParameter)
    {
        var action = () => new NewMarketAssetRecord(itemIndex, assetId, outcome);

        action.Should().Throw<ArgumentException>()
            .WithParameterName(expectedParameter);
    }

    [Fact]
    public void MarketResolvedRecords_ShouldPreserveConfirmedContractAndOrderedAsset()
    {
        var resolution = new MarketResolvedRecord("market-id", "winning-asset-id", "Up");
        var asset = new MarketResolvedAssetRecord(1, "asset-id");

        resolution.ExternalMarketId.Should().Be("market-id");
        resolution.WinningAssetId.Should().Be("winning-asset-id");
        resolution.WinningOutcome.Should().Be("Up");
        asset.ItemIndex.Should().Be(1);
        asset.AssetId.Should().Be("asset-id");
    }

    [Theory]
    [InlineData("", "asset", "Up", "externalMarketId")]
    [InlineData("market", "", "Up", "winningAssetId")]
    [InlineData("market", "asset", "", "winningOutcome")]
    public void MarketResolvedRecord_EmptyRequiredValue_ShouldRejectInvalidState(
        string externalMarketId,
        string winningAssetId,
        string winningOutcome,
        string expectedParameter)
    {
        var action = () => new MarketResolvedRecord(externalMarketId, winningAssetId, winningOutcome);

        action.Should().Throw<ArgumentException>()
            .WithParameterName(expectedParameter);
    }

    [Theory]
    [InlineData(-1, "asset", "itemIndex")]
    [InlineData(0, "", "assetId")]
    public void MarketResolvedAssetRecord_InvalidValue_ShouldRejectInvalidState(
        int itemIndex,
        string assetId,
        string expectedParameter)
    {
        var action = () => new MarketResolvedAssetRecord(itemIndex, assetId);

        action.Should().Throw<ArgumentException>()
            .WithParameterName(expectedParameter);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("1")]
    public void LogicalRawEvent_WithoutObjectJson_ShouldRejectInvalidState(string json)
    {
        using var document = JsonDocument.Parse(json);

        var action = () => new LogicalRawEvent(
            rawMessageId: 42,
            rawItemIndex: 0,
            projectionVersion: 1,
            sessionId: CreateSessionId(),
            receivedAt: DateTimeOffset.Parse("2026-08-10T10:00:00Z"),
            json: document.RootElement);

        action.Should().Throw<ArgumentException>()
            .WithParameterName("json");
    }

    private static NormalizedEvent CreateEvent(
        IReadOnlyCollection<NormalizedRecord>? records = null)
    {
        return new NormalizedEvent(
            rawMessageId: 42,
            rawItemIndex: 1,
            projectionVersion: 2,
            normalizerVersion: 3,
            eventType: "book",
            sessionId: CreateSessionId(),
            receivedAt: DateTimeOffset.Parse("2026-08-10T10:00:00Z"),
            sourceTimestamp: 1786356000000,
            marketConditionId: "0xcondition",
            assetId: "123",
            records: records ?? [new StubNormalizedRecord("first")]);
    }

    private static CollectorSessionId CreateSessionId()
    {
        return CollectorSessionId.Create(
            Guid.Parse("6d9ac447-7bcc-4c85-8619-0384da429a33")).Value;
    }

    private sealed record StubNormalizedRecord(string Value) : NormalizedRecord;
}
