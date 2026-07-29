using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.RawMarketMessage;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.Postgres;

public sealed class RawMarketMessageWriterTests
{
    [Fact]
    public async Task WriteBatchAsync_ShouldPersistMessagesAndCopyPayloads()
    {
        await using var context = CreateContext();
        var writer = new RawMarketMessageWriter(context);
        var sessionId = CollectorSessionId.Create(Guid.NewGuid()).Value;
        var firstPayload = "first"u8.ToArray();
        var messages = new RawMarketMessage[]
        {
            new(sessionId, DateTimeOffset.Parse("2026-07-27T10:00:00Z"), firstPayload),
            new(sessionId, DateTimeOffset.Parse("2026-07-27T10:00:01Z"), "second"u8.ToArray())
        };

        await writer.WriteBatchAsync(messages, CancellationToken.None);
        firstPayload[0] = (byte)'X';

        var records = await context.RawMarketMessages
            .OrderBy(message => message.ReceivedAt)
            .ToListAsync();
        records.Should().HaveCount(2);
        records.Select(message => message.SessionId)
            .Should()
            .OnlyContain(id => id == sessionId);
        records.Select(message => message.ReceivedAt)
            .Should()
            .BeInAscendingOrder();
        records[0].Payload.Should().Equal("first"u8.ToArray());
        records[1].Payload.Should().Equal("second"u8.ToArray());
    }

    [Fact]
    public async Task WriteBatchAsync_WithEmptyBatch_ShouldNotTrackRecords()
    {
        await using var context = CreateContext();
        var writer = new RawMarketMessageWriter(context);

        await writer.WriteBatchAsync([], CancellationToken.None);

        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task WriteBatchAsync_WhenCancelled_ShouldPropagateCancellation()
    {
        await using var context = CreateContext();
        var writer = new RawMarketMessageWriter(context);
        var message = new RawMarketMessage(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            DateTimeOffset.UtcNow,
            "payload"u8.ToArray());
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        Func<Task> write = () => writer.WriteBatchAsync(
            [message],
            cancellationTokenSource.Token);

        await write.Should().ThrowAsync<OperationCanceledException>();
    }

    private static DataCollectionDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DataCollectionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new DataCollectionDbContext(options);
    }
}
