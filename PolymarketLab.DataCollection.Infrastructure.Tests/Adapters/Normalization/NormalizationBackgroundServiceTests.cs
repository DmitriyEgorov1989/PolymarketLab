using System.Diagnostics;
using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Application.Normalization;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.Normalization;

public sealed class NormalizationBackgroundServiceTests
{
    [Fact]
    public async Task ExecuteAsync_WhenDisabled_ShouldNotResolveProcessor()
    {
        await using var fixture = CreateFixture(
            new NormalizerOptions { Enabled = false },
            (_, _) => Task.FromResult(ProcessedBatch()));

        await fixture.Worker.StartAsync(default);
        await fixture.Worker.StopAsync(default);

        fixture.State.InstanceCount.Should().Be(0);
        fixture.State.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_NonEmptyBatches_ShouldContinueWithNewScopeWithoutDelay()
    {
        await using var fixture = CreateFixture(
            new NormalizerOptions { IdleDelay = TimeSpan.FromHours(1) },
            (call, cancellationToken) => call <= 2
                ? Task.FromResult(ProcessedBatch())
                : Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                    .ContinueWith(
                        _ => ProcessedBatch(),
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default));

        await fixture.Worker.StartAsync(default);
        await fixture.State.WaitForCallAsync();
        await fixture.State.WaitForCallAsync();
        await fixture.State.WaitForCallAsync();

        fixture.State.InstanceCount.Should().Be(3);
        fixture.State.DisposeCount.Should().Be(2);
        await fixture.Worker.StopAsync(default);
        fixture.State.DisposeCount.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyBatch_ShouldWaitConfiguredIdleDelay()
    {
        var releaseFirstBatch = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = CreateFixture(
            new NormalizerOptions { IdleDelay = TimeSpan.FromHours(1) },
            async (call, _) =>
            {
                if (call == 1)
                    await releaseFirstBatch.Task;
                return EmptyBatch();
            });

        await fixture.Worker.StartAsync(default);
        await fixture.State.WaitForCallAsync();
        var secondCall = fixture.State.WaitForCallAsync();
        releaseFirstBatch.TrySetResult();

        await Task.Delay(100);
        secondCall.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task StopAsync_DuringActiveBatch_ShouldCancelProcessorAndDisposeScope()
    {
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = CreateFixture(
            new NormalizerOptions(),
            async (_, cancellationToken) =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return ProcessedBatch();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    cancellationObserved.TrySetResult();
                    throw;
                }
            });
        await fixture.Worker.StartAsync(default);
        await fixture.State.WaitForCallAsync();

        await fixture.Worker.StopAsync(default);

        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));
        fixture.State.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task StopAsync_DuringIdleDelay_ShouldCompletePromptly()
    {
        await using var fixture = CreateFixture(
            new NormalizerOptions { IdleDelay = TimeSpan.FromHours(1) },
            (_, _) => Task.FromResult(EmptyBatch()));
        await fixture.Worker.StartAsync(default);
        await fixture.State.WaitForCallAsync();
        var stopwatch = Stopwatch.StartNew();

        await fixture.Worker.StopAsync(default);

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        fixture.State.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenIterationFails_ShouldBackOffAndContinue()
    {
        await using var fixture = CreateFixture(
            new NormalizerOptions(),
            (call, cancellationToken) => call == 1
                ? throw new InvalidOperationException("Database is unavailable.")
                : Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                    .ContinueWith(
                        _ => ProcessedBatch(),
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default));
        await fixture.Worker.StartAsync(default);
        await fixture.State.WaitForCallAsync();
        var secondCall = fixture.State.WaitForCallAsync();

        await Task.Delay(100);
        fixture.State.CallCount.Should().Be(1);
        await secondCall.WaitAsync(TimeSpan.FromSeconds(2));
        fixture.State.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task StopAsync_DuringExceptionBackoff_ShouldCompletePromptly()
    {
        await using var fixture = CreateFixture(
            new NormalizerOptions(),
            (_, _) => throw new InvalidOperationException("Database is unavailable."));
        await fixture.Worker.StartAsync(default);
        await fixture.State.WaitForCallAsync();
        var stopwatch = Stopwatch.StartNew();

        await fixture.Worker.StopAsync(default);

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        fixture.State.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_NonEmptyBatch_ShouldLogSummaryAndSafeMessageError()
    {
        var logger = new CapturingLogger<NormalizationBackgroundService>();
        var sessionId = CollectorSessionId.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111")).Value;
        var result = new NormalizationBatchResult(
            1,
            0,
            1,
            0,
            0,
            10,
            10,
            [
                new NormalizationMessageError(
                    10,
                    sessionId,
                    2,
                    "book",
                    3,
                    1,
                    NormalizationStatus.Invalid,
                    "normalization.book.invalid")
            ]);
        await using var fixture = CreateFixture(
            new NormalizerOptions
            {
                ProjectionVersion = 3,
                BatchSize = 25,
                IdleDelay = TimeSpan.FromHours(1)
            },
            (call, cancellationToken) => call == 1
                ? Task.FromResult(result)
                : Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                    .ContinueWith(
                        _ => EmptyBatch(),
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default),
            logger);

        await fixture.Worker.StartAsync(default);
        await fixture.State.WaitForCallAsync();
        await fixture.State.WaitForCallAsync();

        var summary = logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Information).Which.Properties;
        summary.Should().Contain(new KeyValuePair<string, object?>("ProjectionVersion", 3));
        summary.Should().Contain(new KeyValuePair<string, object?>("BatchSize", 1));
        summary.Should().ContainKey("DurationMs");
        var messageError = logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Warning).Which.Properties;
        messageError.Should().Contain(new KeyValuePair<string, object?>("RawMessageId", 10L));
        messageError.Should().Contain(new KeyValuePair<string, object?>("SessionId", sessionId.Value));
        messageError.Should().Contain(new KeyValuePair<string, object?>("RawItemIndex", 2));
        messageError.Should().Contain(new KeyValuePair<string, object?>("EventType", "book"));
        messageError.Should().Contain(new KeyValuePair<string, object?>("NormalizerVersion", 1));
        messageError.Should().Contain(new KeyValuePair<string, object?>(
            "ErrorCode",
            "normalization.book.invalid"));
        logger.Entries.Select(entry => entry.Message).Should().NotContain(message =>
            message.Contains("payload", StringComparison.OrdinalIgnoreCase)
            || message.Contains("{\"", StringComparison.Ordinal));
    }

    private static WorkerFixture CreateFixture(
        NormalizerOptions options,
        Func<int, CancellationToken, Task<NormalizationBatchResult>> process,
        ILogger<NormalizationBackgroundService>? logger = null)
    {
        var state = new WorkerState(process);
        var services = new ServiceCollection();
        services.AddScoped<INormalizationProcessor>(_ => state.CreateProcessor());
        var provider = services.BuildServiceProvider();
        var telemetry = new NormalizerTelemetry();
        var worker = new NormalizationBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            telemetry,
            Options.Create(options),
            TimeProvider.System,
            logger ?? NullLogger<NormalizationBackgroundService>.Instance);
        return new WorkerFixture(worker, provider, telemetry, state);
    }

    private static NormalizationBatchResult EmptyBatch() =>
        new(0, 0, 0, 0, 0, null, null);

    private static NormalizationBatchResult ProcessedBatch() =>
        new(1, 1, 0, 0, 0, 1, 1);

    private sealed class WorkerState(
        Func<int, CancellationToken, Task<NormalizationBatchResult>> process)
    {
        private readonly Channel<int> calls = Channel.CreateUnbounded<int>();
        private int instanceCount;
        private int callCount;
        private int disposeCount;

        public int InstanceCount => Volatile.Read(ref instanceCount);
        public int CallCount => Volatile.Read(ref callCount);
        public int DisposeCount => Volatile.Read(ref disposeCount);

        public INormalizationProcessor CreateProcessor()
        {
            Interlocked.Increment(ref instanceCount);
            return new StubProcessor(this);
        }

        public async Task WaitForCallAsync() =>
            await calls.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        private Task<NormalizationBatchResult> ProcessAsync(CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref callCount);
            calls.Writer.TryWrite(call);
            return process(call, cancellationToken);
        }

        private sealed class StubProcessor(WorkerState state) : INormalizationProcessor, IDisposable
        {
            public Task<NormalizationBatchResult> ProcessBatchAsync(
                CancellationToken cancellationToken) =>
                state.ProcessAsync(cancellationToken);

            public void Dispose() => Interlocked.Increment(ref state.disposeCount);
        }
    }

    private sealed class WorkerFixture(
        NormalizationBackgroundService worker,
        ServiceProvider provider,
        NormalizerTelemetry telemetry,
        WorkerState state) : IAsyncDisposable
    {
        public NormalizationBackgroundService Worker { get; } = worker;
        public WorkerState State { get; } = state;

        public async ValueTask DisposeAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await Worker.StopAsync(timeout.Token);
            telemetry.Dispose();
            await provider.DisposeAsync();
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.Where(value => value.Key != "{OriginalFormat}").ToDictionary()
                : [];
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), properties));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        IReadOnlyDictionary<string, object?> Properties);
}
