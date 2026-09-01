using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Resolution;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.Resolution;

public sealed class WebSocketResolutionCandidateSourceTests
{
    [Fact]
    public async Task ScanAsync_ShouldAdvanceHighWaterMarkAcrossNonResolutionRows()
    {
        await using var dbContext = CreateContext();
        var sessionId = CollectorSessionId.Create(Guid.NewGuid()).Value;
        var otherSessionId = CollectorSessionId.Create(Guid.NewGuid()).Value;
        dbContext.RawMarketMessages.AddRange(
            Message(sessionId, "not-json"),
            Message(otherSessionId, ResolvedJson),
            Message(sessionId, "{\"event_type\":\"book\"}"),
            Message(sessionId, ResolvedJson));
        await dbContext.SaveChangesAsync();
        var expectedLastId = dbContext.RawMarketMessages
            .Where(message => message.SessionId == sessionId)
            .Max(message => message.Id);

        var result = await new WebSocketResolutionCandidateSource(dbContext)
            .ScanAsync(sessionId, 0, CancellationToken.None);

        result.LastScannedRawMessageId.Should().Be(expectedLastId);
        result.Candidates.Should().ContainSingle();
        result.Candidates.Single().RawMessageId.Should().Be(expectedLastId);
    }

    [Fact]
    public async Task ScanAsync_ShouldKeepCursorWhenNoRowsFollowIt()
    {
        await using var dbContext = CreateContext();
        var sessionId = CollectorSessionId.Create(Guid.NewGuid()).Value;

        var result = await new WebSocketResolutionCandidateSource(dbContext)
            .ScanAsync(sessionId, 123, CancellationToken.None);

        result.LastScannedRawMessageId.Should().Be(123);
        result.Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanAsync_WithBacklogLargerThanBatch_ShouldReachStableHighWaterMark()
    {
        await using var dbContext = CreateContext();
        var sessionId = CollectorSessionId.Create(Guid.NewGuid()).Value;
        var messages = Enumerable.Range(0, 500)
            .Select(_ => Message(sessionId, "{\"event_type\":\"book\"}"))
            .Append(Message(sessionId, ResolvedJson));
        dbContext.RawMarketMessages.AddRange(messages);
        await dbContext.SaveChangesAsync();
        var expectedLastId = dbContext.RawMarketMessages.Max(message => message.Id);

        var result = await new WebSocketResolutionCandidateSource(dbContext)
            .ScanAsync(sessionId, 0, CancellationToken.None);

        result.LastScannedRawMessageId.Should().Be(expectedLastId);
        result.Candidates.Should().ContainSingle(candidate =>
            candidate.RawMessageId == expectedLastId);
    }

    private const string ResolvedJson =
        "{\"event_type\":\"market_resolved\",\"id\":\"market-1\",\"market\":\"condition-1\",\"assets_ids\":[\"token-1\",\"token-2\"],\"winning_asset_id\":\"token-1\",\"winning_outcome\":\"Yes\"}";

    private static RawMarketMessageRecord Message(
        CollectorSessionId sessionId,
        string payload) =>
        new(
            sessionId,
            1,
            new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
            Encoding.UTF8.GetBytes(payload));

    private static DataCollectionDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DataCollectionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new DataCollectionDbContext(options);
    }
}
