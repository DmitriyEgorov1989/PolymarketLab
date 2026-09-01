using System.Text;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.Resolution;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Resolution;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.Resolution;

public sealed class WebSocketResolutionCandidateParserTests
{
    private static readonly DateTimeOffset ReceivedAt =
        new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parse_ShouldReadExactFieldsFromObject()
    {
        var candidates = Parse(
            """
            {"event_type":"market_resolved","id":"market-1","market":"condition-1","assets_ids":["token-1","token-2"],"winning_asset_id":"token-1","winning_outcome":"Yes"}
            """);

        candidates.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            RawMessageId = 17L,
            RawItemIndex = 0,
            ConnectionEpoch = 3L,
            ReceivedAt,
            ExternalMarketId = "market-1",
            ConditionId = "condition-1",
            AssetIds = new[] { "token-1", "token-2" },
            WinningAssetId = "token-1",
            WinningOutcome = "Yes"
        });
    }

    [Fact]
    public void Parse_ShouldPreserveArrayItemIndexAndIgnoreUnrelatedItems()
    {
        var candidates = Parse(
            """
            [{"event_type":"book"},{"event_type":"market_resolved","id":"market-1"}]
            """);

        candidates.Should().ContainSingle();
        candidates.Single().RawItemIndex.Should().Be(1);
    }

    [Fact]
    public void Parse_ShouldReturnIncompleteCandidateForMalformedMarketResolved()
    {
        var candidate = Parse(
            """
            {"event_type":"market_resolved","id":42,"assets_ids":{}}
            """).Single();

        candidate.ExternalMarketId.Should().BeNull();
        candidate.ConditionId.Should().BeNull();
        candidate.AssetIds.Should().BeNull();
        candidate.WinningAssetId.Should().BeNull();
        candidate.WinningOutcome.Should().BeNull();
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"event_type\":42}")]
    [InlineData("42")]
    public void Parse_ShouldIgnoreMalformedUnrelatedRaw(string payload)
    {
        Parse(payload).Should().BeEmpty();
    }

    private static IReadOnlyCollection<WebSocketResolutionCandidate> Parse(
        string payload) =>
        WebSocketResolutionCandidateParser.Parse(
            17,
            3,
            ReceivedAt,
            Encoding.UTF8.GetBytes(payload));
}
