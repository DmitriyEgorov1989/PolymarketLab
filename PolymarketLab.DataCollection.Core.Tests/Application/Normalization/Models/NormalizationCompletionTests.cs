using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.Normalization.Models;

public sealed class NormalizationCompletionTests
{
    [Fact]
    public void Processed_ShouldOwnEventsCopy()
    {
        var events = new List<NormalizedEvent> { CreateEvent(0) };

        var completion = NormalizationCompletion.Processed(events);
        events.Clear();

        completion.Status.Should().Be(NormalizationStatus.Processed);
        completion.Events.Should().ContainSingle();
        completion.Issue.Should().BeNull();
    }

    [Fact]
    public void Processed_WithDuplicateRawItemIndex_ShouldRejectInvalidState()
    {
        var action = () => NormalizationCompletion.Processed(
            [CreateEvent(0), CreateEvent(0)]);

        action.Should().Throw<ArgumentException>()
            .WithParameterName("events");
    }

    [Theory]
    [InlineData(NormalizationStatus.Invalid)]
    [InlineData(NormalizationStatus.Unsupported)]
    [InlineData(NormalizationStatus.Failed)]
    public void NonProcessed_ShouldContainIssueWithoutEvents(NormalizationStatus status)
    {
        var issue = new NormalizationIssue("normalization.issue", "Expected issue.");

        var completion = status switch
        {
            NormalizationStatus.Invalid => NormalizationCompletion.Invalid(issue),
            NormalizationStatus.Unsupported => NormalizationCompletion.Unsupported(issue),
            NormalizationStatus.Failed => NormalizationCompletion.Failed(issue),
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

        completion.Status.Should().Be(status);
        completion.Events.Should().BeEmpty();
        completion.Issue.Should().BeSameAs(issue);
    }

    private static NormalizedEvent CreateEvent(int rawItemIndex) =>
        new(
            rawMessageId: 1,
            rawItemIndex,
            projectionVersion: 1,
            normalizerVersion: 1,
            eventType: "last_trade_price",
            CollectorSessionId.Create(Guid.Parse("11111111-1111-1111-1111-111111111111")).Value,
            receivedAt: DateTimeOffset.Parse("2026-08-14T10:00:00Z"),
            sourceTimestamp: null,
            marketConditionId: null,
            assetId: "asset-1",
            records: [new LastTradeRecord(0.5m, null, TradeSide.Buy, null, null)]);
}
