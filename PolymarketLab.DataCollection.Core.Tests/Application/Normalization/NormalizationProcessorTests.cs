using System.Text;
using System.Text.Json;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.Normalization;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.Normalization;

public sealed class NormalizationProcessorTests
{
    private static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task ProcessBatch_ValidInvalidValid_ShouldContinueAndReturnSummary()
    {
        var claims = new[] { CreateClaim(100), CreateClaim(101), CreateClaim(102) };
        var claimRepository = new StubClaimRepository(claims);
        var decoder = new StubDecoder(claim => claim.RawMessageId == 101
            ? RawMessageDecodeResult.Invalid(new NormalizationIssue(
                "json.invalid",
                "Invalid JSON.",
                "$"))
            : DecodedObject(0));
        var dispatcher = new StubDispatcher(rawEvent =>
            NormalizationResult.Processed(CreateEvent(rawEvent)));
        var writer = new StubWriter();
        var processor = CreateProcessor(claimRepository, decoder, dispatcher, writer);

        var result = await processor.ProcessBatchAsync(default);

        result.Should().BeEquivalentTo(new NormalizationBatchResult(
            3, 2, 1, 0, 0, 100, 102, result.Errors));
        result.Errors.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new NormalizationMessageError(
                101,
                claims[1].Message.SessionId,
                null,
                null,
                1,
                null,
                NormalizationStatus.Invalid,
                "json.invalid",
                "$"));
        writer.Calls.Select(call => call.Completion.Status).Should().Equal(
            NormalizationStatus.Processed,
            NormalizationStatus.Invalid,
            NormalizationStatus.Processed);
        claimRepository.Request.Should().Be((1, 100, ClaimTimeout));
    }

    [Fact]
    public async Task ProcessBatch_UnknownEventType_ShouldPersistUnsupported()
    {
        var issue = new NormalizationIssue("event.unsupported", "Unknown event type.");
        var writer = new StubWriter();
        var processor = CreateProcessor(
            new StubClaimRepository([CreateClaim(1)]),
            new StubDecoder(_ => DecodedObject(0)),
            new StubDispatcher(rawEvent => NormalizationResult.Unsupported(
                rawEvent.RawItemIndex,
                issue)),
            writer);

        var result = await processor.ProcessBatchAsync(default);

        result.Should().BeEquivalentTo(new NormalizationBatchResult(
            1, 0, 0, 1, 0, 1, 1, result.Errors));
        result.Errors.Should().ContainSingle().Which.Should().Match<NormalizationMessageError>(error =>
            error.RawItemIndex == 0
            && error.EventType == "last_trade_price"
            && error.Status == NormalizationStatus.Unsupported
            && error.ErrorCode == "event.unsupported");
        writer.Calls.Should().ContainSingle();
        writer.Calls[0].Completion.Status.Should().Be(NormalizationStatus.Unsupported);
        writer.Calls[0].Completion.Issue.Should().BeSameAs(issue);
    }

    [Fact]
    public async Task ProcessBatch_EmptyArray_ShouldPersistProcessedWithoutEvents()
    {
        var dispatcher = new StubDispatcher(_ => throw new InvalidOperationException());
        var writer = new StubWriter();
        var processor = CreateProcessor(
            new StubClaimRepository([CreateClaim(1)]),
            new StubDecoder(_ => RawMessageDecodeResult.Decoded([])),
            dispatcher,
            writer);

        var result = await processor.ProcessBatchAsync(default);

        result.Processed.Should().Be(1);
        dispatcher.Calls.Should().Be(0);
        writer.Calls.Should().ContainSingle();
        writer.Calls[0].Completion.Status.Should().Be(NormalizationStatus.Processed);
        writer.Calls[0].Completion.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessBatch_ValidArray_ShouldWriteAllItemsOnce()
    {
        var writer = new StubWriter();
        var processor = CreateProcessor(
            new StubClaimRepository([CreateClaim(1)]),
            new StubDecoder(_ => RawMessageDecodeResult.Decoded(
            [
                DecodedItem(0),
                DecodedItem(1)
            ])),
            new StubDispatcher(rawEvent => NormalizationResult.Processed(CreateEvent(rawEvent))),
            writer);

        var result = await processor.ProcessBatchAsync(default);

        result.Processed.Should().Be(1);
        writer.Calls.Should().ContainSingle();
        writer.Calls[0].Completion.Events.Select(item => item.RawItemIndex).Should().Equal(0, 1);
    }

    [Fact]
    public async Task ProcessBatch_MixedArray_ShouldNotLeavePartialResult()
    {
        var unsupportedIssue = new NormalizationIssue("event.unsupported", "Unsupported event.");
        var invalidIssue = new NormalizationIssue("item.invalid", "Invalid array item.");
        var writer = new StubWriter();
        var processor = CreateProcessor(
            new StubClaimRepository([CreateClaim(1)]),
            new StubDecoder(_ => RawMessageDecodeResult.Decoded(
            [
                DecodedItem(0),
                RawMessageItemDecodeResult.Invalid(1, invalidIssue),
                DecodedItem(2)
            ])),
            new StubDispatcher(rawEvent => rawEvent.RawItemIndex == 0
                ? NormalizationResult.Processed(CreateEvent(rawEvent))
                : NormalizationResult.Unsupported(rawEvent.RawItemIndex, unsupportedIssue)),
            writer);

        var result = await processor.ProcessBatchAsync(default);

        result.Should().BeEquivalentTo(new NormalizationBatchResult(
            1, 0, 1, 0, 0, 1, 1, result.Errors));
        result.Errors.Should().ContainSingle().Which.RawItemIndex.Should().Be(1);
        writer.Calls.Should().ContainSingle();
        writer.Calls[0].Completion.Status.Should().Be(NormalizationStatus.Invalid);
        writer.Calls[0].Completion.Events.Should().BeEmpty();
        writer.Calls[0].Completion.Issue.Should().BeSameAs(invalidIssue);
    }

    [Fact]
    public async Task ProcessBatch_UnexpectedMessageError_ShouldPersistFailedAndContinue()
    {
        var writer = new StubWriter();
        var processor = CreateProcessor(
            new StubClaimRepository([CreateClaim(1), CreateClaim(2)]),
            new StubDecoder(_ => DecodedObject(0)),
            new StubDispatcher(rawEvent => rawEvent.RawMessageId == 1
                ? throw new InvalidOperationException("Technical details must not be persisted.")
                : NormalizationResult.Processed(CreateEvent(rawEvent))),
            writer);

        var result = await processor.ProcessBatchAsync(default);

        result.Should().BeEquivalentTo(new NormalizationBatchResult(
            2, 1, 0, 0, 1, 1, 2, result.Errors));
        result.Errors.Should().ContainSingle().Which.ErrorCode.Should()
            .Be("normalization.processing.failed");
        result.Errors.Single().Exception.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("Technical details must not be persisted.");
        writer.Calls.Select(call => call.Completion.Status).Should().Equal(
            NormalizationStatus.Failed,
            NormalizationStatus.Processed);
        writer.Calls[0].Completion.Issue!.Message.Should().NotContain("Technical details");
    }

    [Fact]
    public async Task ProcessBatch_LostClaim_ShouldCountFailed()
    {
        var processor = CreateProcessor(
            new StubClaimRepository([CreateClaim(1)]),
            new StubDecoder(_ => DecodedObject(0)),
            new StubDispatcher(rawEvent => NormalizationResult.Processed(CreateEvent(rawEvent))),
            new StubWriter((_, _) => Task.FromResult(NormalizationWriteStatus.ClaimLost)));

        var result = await processor.ProcessBatchAsync(default);

        result.Should().BeEquivalentTo(new NormalizationBatchResult(
            1, 0, 0, 0, 1, 1, 1, result.Errors));
        result.Errors.Should().ContainSingle().Which.ErrorCode.Should()
            .Be("normalization.write.claim_lost");
    }

    [Fact]
    public async Task ProcessBatch_WriteError_ShouldMarkFailedAndContinue()
    {
        var failedOnce = false;
        var writer = new StubWriter((claim, completion) =>
        {
            if (claim.Message.RawMessageId == 1
                && completion.Status == NormalizationStatus.Processed
                && !failedOnce)
            {
                failedOnce = true;
                throw new InvalidOperationException("Database failure.");
            }

            return Task.FromResult(NormalizationWriteStatus.Written);
        });
        var processor = CreateProcessor(
            new StubClaimRepository([CreateClaim(1), CreateClaim(2)]),
            new StubDecoder(_ => DecodedObject(0)),
            new StubDispatcher(rawEvent => NormalizationResult.Processed(CreateEvent(rawEvent))),
            writer);

        var result = await processor.ProcessBatchAsync(default);

        result.Should().BeEquivalentTo(new NormalizationBatchResult(
            2, 1, 0, 0, 1, 1, 2, result.Errors));
        result.Errors.Should().ContainSingle().Which.ErrorCode.Should()
            .Be("normalization.processing.failed");
        result.Errors.Single().Exception.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("Database failure.");
        writer.Calls.Select(call => call.Completion.Status).Should().Equal(
            NormalizationStatus.Processed,
            NormalizationStatus.Failed,
            NormalizationStatus.Processed);
    }

    [Fact]
    public async Task ProcessBatch_RequestedCancellation_ShouldPropagate()
    {
        using var cancellation = new CancellationTokenSource();
        var writer = new StubWriter();
        var processor = CreateProcessor(
            new StubClaimRepository([CreateClaim(1)]),
            new StubDecoder(_ =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }),
            new StubDispatcher(_ => throw new InvalidOperationException()),
            writer);

        var action = async () => await processor.ProcessBatchAsync(cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        writer.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessBatch_EmptyClaim_ShouldReturnEmptySummary()
    {
        var processor = CreateProcessor(
            new StubClaimRepository([]),
            new StubDecoder(_ => throw new InvalidOperationException()),
            new StubDispatcher(_ => throw new InvalidOperationException()),
            new StubWriter());

        var result = await processor.ProcessBatchAsync(default);

        result.Should().BeEquivalentTo(new NormalizationBatchResult(0, 0, 0, 0, 0, null, null));
    }

    private static NormalizationProcessor CreateProcessor(
        IRawMessageNormalizationClaimRepository claimRepository,
        IRawMessageDecoder decoder,
        INormalizationDispatcher dispatcher,
        INormalizedMessageWriter writer) =>
        new(claimRepository, decoder, dispatcher, writer, 1, 100, ClaimTimeout);

    private static ClaimedRawMessage CreateClaim(long rawMessageId) =>
        new(
            new RawMessageEnvelope(
                rawMessageId,
                CollectorSessionId.Create(Guid.Parse("11111111-1111-1111-1111-111111111111")).Value,
                DateTimeOffset.Parse("2026-08-14T10:00:00Z").AddSeconds(rawMessageId),
                Encoding.UTF8.GetBytes("{}")),
            ProjectionVersion: 1,
            AttemptCount: 1);

    private static RawMessageDecodeResult DecodedObject(int rawItemIndex) =>
        RawMessageDecodeResult.Decoded([DecodedItem(rawItemIndex)]);

    private static RawMessageItemDecodeResult DecodedItem(int rawItemIndex)
    {
        using var document = JsonDocument.Parse("{\"event_type\":\"last_trade_price\"}");
        return RawMessageItemDecodeResult.Decoded(rawItemIndex, document.RootElement);
    }

    private static NormalizedEvent CreateEvent(LogicalRawEvent rawEvent) =>
        new(
            rawEvent.RawMessageId,
            rawEvent.RawItemIndex,
            rawEvent.ProjectionVersion,
            normalizerVersion: 1,
            eventType: "last_trade_price",
            rawEvent.SessionId,
            rawEvent.ReceivedAt,
            sourceTimestamp: null,
            marketConditionId: null,
            assetId: "asset-1",
            records: [new LastTradeRecord(0.5m, null, TradeSide.Buy, null, null)]);

    private sealed class StubClaimRepository(IReadOnlyList<ClaimedRawMessage> claims)
        : IRawMessageNormalizationClaimRepository
    {
        public (int ProjectionVersion, int BatchSize, TimeSpan ClaimTimeout)? Request { get; private set; }

        public Task<IReadOnlyList<ClaimedRawMessage>> ClaimBatchAsync(
            int projectionVersion,
            int batchSize,
            TimeSpan claimTimeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = (projectionVersion, batchSize, claimTimeout);
            return Task.FromResult(claims);
        }
    }

    private sealed class StubDecoder(Func<RawMessageEnvelope, RawMessageDecodeResult> decode)
        : IRawMessageDecoder
    {
        public RawMessageDecodeResult Decode(RawMessageEnvelope message) => decode(message);
    }

    private sealed class StubDispatcher(Func<LogicalRawEvent, NormalizationResult> dispatch)
        : INormalizationDispatcher
    {
        public int Calls { get; private set; }

        public NormalizationResult Dispatch(LogicalRawEvent rawEvent)
        {
            Calls++;
            return dispatch(rawEvent);
        }
    }

    private sealed class StubWriter(
        Func<ClaimedRawMessage, NormalizationCompletion, Task<NormalizationWriteStatus>>? write = null)
        : INormalizedMessageWriter
    {
        public List<WriteCall> Calls { get; } = [];

        public async Task<NormalizationWriteStatus> WriteAsync(
            ClaimedRawMessage claim,
            NormalizationCompletion completion,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(new WriteCall(claim, completion));
            return write is null
                ? NormalizationWriteStatus.Written
                : await write(claim, completion);
        }
    }

    private sealed record WriteCall(
        ClaimedRawMessage Claim,
        NormalizationCompletion Completion);
}
