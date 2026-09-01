using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using System.Collections.Concurrent;
using System.Threading.Channels;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.RawMessageIngestion;

public sealed class RawMarketMessagePersistenceWorkerTests
{
    [Fact]
    public async Task ExecuteAsync_WhenBatchIsFull_ShouldPersistWithNewScopePerBatch()
    {
        await using var fixture = CreateFixture(new RawMessageIngestionOptions
        {
            Capacity = 10,
            BatchSize = 2,
            FlushInterval = TimeSpan.FromHours(1)
        });
        await fixture.Worker.StartAsync(CancellationToken.None);
        var firstSessionId = CollectorSessionId.Create(Guid.NewGuid()).Value;
        var secondSessionId = CollectorSessionId.Create(Guid.NewGuid()).Value;

        for (var value = 1; value <= 4; value++)
        {
            await fixture.Channel.EnqueueAsync(
                CreateMessage(value, value <= 2 ? firstSessionId : secondSessionId),
                CancellationToken.None);
        }

        var firstBatch = await fixture.State.WaitForBatchAsync();
        var secondBatch = await fixture.State.WaitForBatchAsync();

        firstBatch.Select(ReadValue).Should().Equal(1, 2);
        secondBatch.Select(ReadValue).Should().Equal(3, 4);
        fixture.State.WriterInstanceCount.Should().Be(2);
        fixture.Telemetry.GetSnapshot(firstSessionId).Persisted.Should().Be(2);
        fixture.Telemetry.GetSnapshot(secondSessionId).Persisted.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFlushIntervalElapses_ShouldPersistPartialBatch()
    {
        await using var fixture = CreateFixture(new RawMessageIngestionOptions
        {
            Capacity = 10,
            BatchSize = 10,
            FlushInterval = TimeSpan.FromMilliseconds(20)
        });
        await fixture.Worker.StartAsync(CancellationToken.None);

        await fixture.Channel.EnqueueAsync(
            CreateMessage(1),
            CancellationToken.None);

        var batch = await fixture.State.WaitForBatchAsync();
        batch.Select(ReadValue).Should().Equal(1);
    }

    [Fact]
    public async Task StopAsync_ShouldDrainPartialBatch()
    {
        await using var fixture = CreateFixture(new RawMessageIngestionOptions
        {
            Capacity = 10,
            BatchSize = 10,
            FlushInterval = TimeSpan.FromHours(1),
            ShutdownTimeout = TimeSpan.FromSeconds(1)
        });
        await fixture.Worker.StartAsync(CancellationToken.None);
        await fixture.Channel.EnqueueAsync(
            CreateMessage(1),
            CancellationToken.None);

        await fixture.Worker.StopAsync(CancellationToken.None);

        fixture.Worker.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
        fixture.Lifetime.StopCallCount.Should().Be(0);
        fixture.State.WriterInstanceCount.Should().Be(1);
        var batch = await fixture.State.WaitForBatchAsync();
        batch.Select(ReadValue).Should().Equal(1);
    }

    [Fact]
    public async Task StopAsync_WithCancelledHostToken_ShouldStillDrainPartialBatch()
    {
        await using var fixture = CreateFixture(new RawMessageIngestionOptions
        {
            Capacity = 10,
            BatchSize = 10,
            FlushInterval = TimeSpan.FromHours(1),
            ShutdownTimeout = TimeSpan.FromSeconds(1)
        });
        await fixture.Worker.StartAsync(CancellationToken.None);
        await fixture.Channel.EnqueueAsync(
            CreateMessage(1),
            CancellationToken.None);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await fixture.Worker.StopAsync(cancellationTokenSource.Token);

        fixture.Worker.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
        var batch = await fixture.State.WaitForBatchAsync();
        batch.Select(ReadValue).Should().Equal(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenWriterFails_ShouldStopApplicationAndCompleteChannel()
    {
        await using var fixture = CreateFixture(new RawMessageIngestionOptions
        {
            Capacity = 10,
            BatchSize = 1,
            FlushInterval = TimeSpan.FromHours(1)
        });
        fixture.State.Handler = (_, _) => throw new InvalidOperationException(
            "Persistence failure.");
        await fixture.Worker.StartAsync(CancellationToken.None);

        await fixture.Channel.EnqueueAsync(
            CreateMessage(1),
            CancellationToken.None);

        Func<Task> completion = async () => await fixture.Worker.ExecuteTask!;
        await completion.Should().ThrowAsync<InvalidOperationException>();
        fixture.Lifetime.StopCallCount.Should().Be(1);

        Func<Task> enqueue = async () => await fixture.Channel.EnqueueAsync(
            CreateMessage(2),
            CancellationToken.None);
        await enqueue.Should().ThrowAsync<ChannelClosedException>();
    }

    [Fact]
    public async Task StopAsync_WhenDrainTimesOut_ShouldCancelPersistence()
    {
        var writeStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = CreateFixture(new RawMessageIngestionOptions
        {
            Capacity = 10,
            BatchSize = 1,
            FlushInterval = TimeSpan.FromHours(1),
            ShutdownTimeout = TimeSpan.FromMilliseconds(20)
        });
        fixture.State.Handler = async (_, cancellationToken) =>
        {
            writeStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        };
        await fixture.Worker.StartAsync(CancellationToken.None);
        await fixture.Channel.EnqueueAsync(
            CreateMessage(1),
            CancellationToken.None);
        await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await fixture.Worker.StopAsync(CancellationToken.None);

        await fixture.Worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(2));
        fixture.Worker.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_WhenWriterIgnoresCancellation_ShouldRespectTimeout()
    {
        var writeStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = CreateFixture(new RawMessageIngestionOptions
        {
            Capacity = 10,
            BatchSize = 1,
            FlushInterval = TimeSpan.FromHours(1),
            ShutdownTimeout = TimeSpan.FromMilliseconds(20)
        });
        fixture.State.Handler = async (_, _) =>
        {
            writeStarted.SetResult();
            await releaseWrite.Task;
        };
        await fixture.Worker.StartAsync(CancellationToken.None);
        await fixture.Channel.EnqueueAsync(
            CreateMessage(1),
            CancellationToken.None);
        await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await fixture.Worker
            .StopAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));

        fixture.Worker.ExecuteTask!.IsCompleted.Should().BeFalse();
        releaseWrite.SetResult();
        await fixture.Worker.ExecuteTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Completion_ShouldCompleteOnlyAfterChannelDrainedAndFinalFlushSucceeded()
    {
        await using var fixture = CreateFixture(new RawMessageIngestionOptions
        {
            Capacity = 10,
            BatchSize = 10,
            FlushInterval = TimeSpan.FromHours(1),
            ShutdownTimeout = TimeSpan.FromSeconds(1)
        });
        await fixture.Worker.StartAsync(CancellationToken.None);
        await fixture.Channel.EnqueueAsync(CreateMessage(1), CancellationToken.None);

        fixture.Worker.Completion.IsCompleted.Should().BeFalse();

        await fixture.Worker.StopAsync(CancellationToken.None);
        var completion = await fixture.Worker.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        completion.Result.IsSuccess.Should().BeTrue();
        completion.UnconfirmedMessageCount.Should().Be(0);
        var batch = await fixture.State.WaitForBatchAsync();
        batch.Select(ReadValue).Should().Equal(1);
    }

    [Fact]
    public async Task Completion_WhenFinalFlushFails_ShouldCompleteWithFailure()
    {
        await using var fixture = CreateFixture(new RawMessageIngestionOptions
        {
            Capacity = 10,
            BatchSize = 10,
            FlushInterval = TimeSpan.FromHours(1),
            ShutdownTimeout = TimeSpan.FromSeconds(1)
        });
        fixture.State.Handler = (_, _) => throw new InvalidOperationException(
            "Final flush failed.");
        await fixture.Worker.StartAsync(CancellationToken.None);
        await fixture.Channel.EnqueueAsync(CreateMessage(1), CancellationToken.None);

        await fixture.Worker.StopAsync(CancellationToken.None);
        var completion = await fixture.Worker.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        completion.Result.IsFailure.Should().BeTrue();
        completion.Result.Error.Code.Should().Be("raw_messages.persistence.failed");
        completion.UnconfirmedMessageCount.Should().Be(1);
        fixture.Lifetime.StopCallCount.Should().Be(1);
        fixture.Telemetry.GetSnapshot(
                fixture.State.LastAttemptedSessionId!)
            .Persisted
            .Should()
            .Be(0);
    }

    [Fact]
    public async Task StopAsync_WithCancelledHostToken_ShouldNotCancelFinalFlushToken()
    {
        CancellationToken observedToken = default;
        await using var fixture = CreateFixture(new RawMessageIngestionOptions
        {
            Capacity = 10,
            BatchSize = 10,
            FlushInterval = TimeSpan.FromHours(1),
            ShutdownTimeout = TimeSpan.FromSeconds(1)
        });
        fixture.State.Handler = (_, cancellationToken) =>
        {
            observedToken = cancellationToken;
            return Task.CompletedTask;
        };
        await fixture.Worker.StartAsync(CancellationToken.None);
        await fixture.Channel.EnqueueAsync(CreateMessage(1), CancellationToken.None);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await fixture.Worker.StopAsync(cancellationTokenSource.Token);

        observedToken.CanBeCanceled.Should().BeTrue();
        observedToken.IsCancellationRequested.Should().BeFalse();
        (await fixture.Worker.Completion).Result.IsSuccess.Should().BeTrue();
    }

    private static WorkerFixture CreateFixture(RawMessageIngestionOptions options)
    {
        var services = new ServiceCollection();
        var state = new RecordingWriterState();
        services.AddSingleton(state);
        services.AddScoped<IRawMarketMessageWriter, RecordingWriter>();
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        var channel = new RawMarketMessageChannel(Options.Create(options));
        var telemetry = new RawMarketMessageTelemetry();
        var lifetime = new StubHostApplicationLifetime();
        var worker = new RawMarketMessagePersistenceWorker(
            channel,
            telemetry,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            TimeProvider.System,
            lifetime,
            NullLogger<RawMarketMessagePersistenceWorker>.Instance);

        return new WorkerFixture(provider, channel, worker, state, lifetime, telemetry);
    }

    private static RawMarketMessage CreateMessage(
        int value,
        CollectorSessionId? sessionId = null)
    {
        return new RawMarketMessage(
            sessionId ?? CollectorSessionId.Create(Guid.NewGuid()).Value,
            1,
            DateTimeOffset.UtcNow,
            BitConverter.GetBytes(value));
    }

    private static int ReadValue(RawMarketMessage message)
    {
        return BitConverter.ToInt32(message.Payload);
    }

    private sealed class RecordingWriter : IRawMarketMessageWriter
    {
        private readonly RecordingWriterState _state;

        public RecordingWriter(RecordingWriterState state)
        {
            _state = state;
            state.RecordWriterCreated();
        }

        public Task WriteBatchAsync(
            IReadOnlyCollection<RawMarketMessage> messages,
            IReadOnlyCollection<CollectorSessionProgressCheckpoint> checkpoints,
            CancellationToken cancellationToken)
        {
            return _state.WriteAsync(messages, cancellationToken);
        }
    }

    private sealed class RecordingWriterState
    {
        private readonly ConcurrentQueue<RawMarketMessage[]> _batches = new();
        private readonly SemaphoreSlim _batchAvailable = new(0);
        private int _writerInstanceCount;

        public Func<IReadOnlyCollection<RawMarketMessage>, CancellationToken, Task>?
            Handler { get; set; }
        public int WriterInstanceCount => _writerInstanceCount;
        public CollectorSessionId? LastAttemptedSessionId { get; private set; }

        public RecordingWriterState()
        {
        }

        public async Task WriteAsync(
            IReadOnlyCollection<RawMarketMessage> messages,
            CancellationToken cancellationToken)
        {
            LastAttemptedSessionId = messages.FirstOrDefault()?.SessionId;

            if (Handler is not null)
                await Handler(messages, cancellationToken);

            _batches.Enqueue(messages.ToArray());
            _batchAvailable.Release();
        }

        public void RecordWriterCreated()
        {
            Interlocked.Increment(ref _writerInstanceCount);
        }

        public async Task<RawMarketMessage[]> WaitForBatchAsync()
        {
            if (!await _batchAvailable.WaitAsync(TimeSpan.FromSeconds(2)))
                throw new TimeoutException("A persisted batch was not observed.");

            _batches.TryDequeue(out var batch).Should().BeTrue();
            return batch!;
        }
    }

    private sealed class StubHostApplicationLifetime : IHostApplicationLifetime
    {
        public int StopCallCount { get; private set; }
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
            StopCallCount++;
        }
    }

    private sealed class WorkerFixture(
        ServiceProvider provider,
        RawMarketMessageChannel channel,
        RawMarketMessagePersistenceWorker worker,
        RecordingWriterState state,
        StubHostApplicationLifetime lifetime,
        RawMarketMessageTelemetry telemetry)
        : IAsyncDisposable
    {
        public RawMarketMessageChannel Channel { get; } = channel;
        public RawMarketMessagePersistenceWorker Worker { get; } = worker;
        public RecordingWriterState State { get; } = state;
        public StubHostApplicationLifetime Lifetime { get; } = lifetime;
        public RawMarketMessageTelemetry Telemetry { get; } = telemetry;

        public async ValueTask DisposeAsync()
        {
            if (Worker.ExecuteTask is { IsCompleted: false })
            {
                using var cancellationTokenSource =
                    new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await Worker.StopAsync(cancellationTokenSource.Token);
            }

            Worker.Dispose();
            Telemetry.Dispose();
            await provider.DisposeAsync();
        }
    }
}
