using FluentAssertions;
using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using System.Threading.Channels;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.RawMessageIngestion;

public sealed class RawMarketMessageChannelTests
{
    [Fact]
    public async Task EnqueueAsync_ShouldPreserveFifoOrder()
    {
        var channel = CreateChannel(3);
        var messages = new[] { CreateMessage(1), CreateMessage(2), CreateMessage(3) };

        foreach (var message in messages)
            await channel.EnqueueAsync(message, CancellationToken.None);

        var received = new List<RawMarketMessage>();
        while (channel.Reader.TryRead(out var message))
            received.Add(message);

        received.Select(ReadValue).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task EnqueueAsync_ShouldTakeOwnershipOfPayload()
    {
        var channel = CreateChannel(1);
        var payload = BitConverter.GetBytes(1);
        var message = new RawMarketMessage(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            DateTimeOffset.UtcNow,
            payload);

        await channel.EnqueueAsync(message, CancellationToken.None);
        payload[0] = byte.MaxValue;

        var received = await channel.Reader.ReadAsync();
        BitConverter.ToInt32(received.Payload).Should().Be(1);
    }

    [Fact]
    public async Task EnqueueAsync_WhenFull_ShouldWaitForCapacity()
    {
        var channel = CreateChannel(1);
        var first = CreateMessage(1);
        var second = CreateMessage(2);
        await channel.EnqueueAsync(first, CancellationToken.None);

        var blockedWrite = channel
            .EnqueueAsync(second, CancellationToken.None)
            .AsTask();

        blockedWrite.IsCompleted.Should().BeFalse();
        ReadValue(await channel.Reader.ReadAsync()).Should().Be(1);
        await blockedWrite;
        ReadValue(await channel.Reader.ReadAsync()).Should().Be(2);
    }

    [Fact]
    public async Task EnqueueAsync_WhenWaitIsCancelled_ShouldKeepExistingMessage()
    {
        var channel = CreateChannel(1);
        var first = CreateMessage(1);
        await channel.EnqueueAsync(first, CancellationToken.None);
        using var cancellationTokenSource = new CancellationTokenSource();

        var blockedWrite = channel
            .EnqueueAsync(CreateMessage(2), cancellationTokenSource.Token)
            .AsTask();
        cancellationTokenSource.Cancel();

        Func<Task> write = async () => await blockedWrite;
        await write.Should().ThrowAsync<OperationCanceledException>();
        channel.QueuedCount.Should().Be(1);
        channel.TryRead(out var message).Should().BeTrue();
        ReadValue(message).Should().Be(1);
        channel.QueuedCount.Should().Be(0);
    }

    [Fact]
    public async Task EnqueueAsync_AfterCompletion_ShouldRejectMessage()
    {
        var channel = CreateChannel(1);
        channel.TryComplete().Should().BeTrue();

        Func<Task> write = async () =>
            await channel.EnqueueAsync(CreateMessage(1), CancellationToken.None);

        await write.Should().ThrowAsync<ChannelClosedException>();
    }

    [Fact]
    public async Task EnqueueAsync_WithMultipleProducers_ShouldKeepAllMessages()
    {
        const int producerCount = 4;
        const int messagesPerProducer = 25;
        var channel = CreateChannel(producerCount * messagesPerProducer);

        var producers = Enumerable.Range(0, producerCount)
            .Select(producer => Task.Run(async () =>
            {
                for (var index = 0; index < messagesPerProducer; index++)
                {
                    await channel.EnqueueAsync(
                        CreateMessage(producer * messagesPerProducer + index),
                        CancellationToken.None);
                }
            }));
        await Task.WhenAll(producers);

        var payloads = new List<int>();
        while (channel.Reader.TryRead(out var message))
            payloads.Add(BitConverter.ToInt32(message.Payload));

        payloads.Should().BeEquivalentTo(
            Enumerable.Range(0, producerCount * messagesPerProducer));
    }

    private static RawMarketMessageChannel CreateChannel(int capacity)
    {
        return new RawMarketMessageChannel(Options.Create(
            new RawMessageIngestionOptions
            {
                Capacity = capacity,
                BatchSize = capacity
            }));
    }

    private static RawMarketMessage CreateMessage(int value)
    {
        return new RawMarketMessage(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            DateTimeOffset.UtcNow,
            BitConverter.GetBytes(value));
    }

    private static int ReadValue(RawMarketMessage message)
    {
        return BitConverter.ToInt32(message.Payload);
    }
}
