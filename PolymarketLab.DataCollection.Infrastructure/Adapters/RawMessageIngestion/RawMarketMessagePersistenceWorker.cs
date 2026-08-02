using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;

internal sealed class RawMarketMessagePersistenceWorker(
    RawMarketMessageChannel channel,
    RawMarketMessageTelemetry telemetry,
    IServiceScopeFactory scopeFactory,
    IOptions<RawMessageIngestionOptions> options,
    TimeProvider timeProvider,
    IHostApplicationLifetime applicationLifetime,
    ILogger<RawMarketMessagePersistenceWorker> logger)
    : IHostedService, IRawMessagePersistenceCompletion, IDisposable
{
    private readonly RawMessageIngestionOptions _options = options.Value;
    private readonly CancellationTokenSource _drainCts = new();
    private readonly TaskCompletionSource<RawMessagePersistenceCompletionResult> _completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _executeTask;
    private int _stopRequested;
    private int _inFlightMessageCount;
    private int _disposed;

    internal Task? ExecuteTask => _executeTask;
    public Task<RawMessagePersistenceCompletionResult> Completion => _completion.Task;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _executeTask = ExecuteAsync();

        return _executeTask.IsCompleted
            ? _executeTask
            : Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CompleteProducers();

        if (_executeTask is not null)
        {
            using var shutdownCts = new CancellationTokenSource(
                _options.ShutdownTimeout);
            await WaitForCompletionAsync(shutdownCts.Token);
        }
    }

    public void CompleteProducers()
    {
        if (Interlocked.Exchange(ref _stopRequested, 1) == 0)
            channel.TryComplete();
    }

    public async Task<RawMessagePersistenceCompletionResult> WaitForCompletionAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await Completion.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var result = RawMessagePersistenceCompletionResult.Failure(
                RawMessagePersistenceErrors.DrainTimedOut(_options.ShutdownTimeout),
                GetUnconfirmedMessageCount());
            _completion.TrySetResult(result);
            _drainCts.Cancel();

            if (_executeTask is not null)
                _ = ObserveCompletionAsync(_executeTask);

            logger.LogWarning(
                "Raw market message persistence did not complete before shutdown deadline. Unconfirmed messages: {UnconfirmedMessageCount}.",
                result.UnconfirmedMessageCount);

            return await Completion;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        channel.TryComplete();
        _drainCts.Cancel();

        if (_executeTask is { IsCompleted: false })
            _ = DisposeCancellationSourceWhenCompletedAsync(_executeTask);
        else
            _drainCts.Dispose();
    }

    private async Task ExecuteAsync()
    {
        try
        {
            await ConsumeAsync(_drainCts.Token);
            _completion.TrySetResult(
                RawMessagePersistenceCompletionResult.Success(channel.QueuedCount));
        }
        catch (OperationCanceledException)
            when (_drainCts.IsCancellationRequested)
        {
            _completion.TrySetResult(RawMessagePersistenceCompletionResult.Failure(
                RawMessagePersistenceErrors.DrainTimedOut(_options.ShutdownTimeout),
                GetUnconfirmedMessageCount()));
            logger.LogWarning(
                "Raw market message persistence did not drain within {ShutdownTimeout}. Unconfirmed messages: {UnconfirmedMessageCount}.",
                _options.ShutdownTimeout,
                GetUnconfirmedMessageCount());
        }
        catch (Exception exception)
        {
            _completion.TrySetResult(RawMessagePersistenceCompletionResult.Failure(
                RawMessagePersistenceErrors.PersistenceFailed,
                GetUnconfirmedMessageCount()));
            channel.TryComplete(exception);
            logger.LogCritical(
                exception,
                "Raw market message persistence failed; stopping the application. Unconfirmed messages: {UnconfirmedMessageCount}.",
                GetUnconfirmedMessageCount());
            applicationLifetime.StopApplication();
            throw;
        }
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        var batch = new List<RawMarketMessage>(_options.BatchSize);
        using var flushTimer = new PeriodicTimer(
            _options.FlushInterval,
            timeProvider);
        var nextFlush = flushTimer.WaitForNextTickAsync(cancellationToken).AsTask();
        Task<bool>? messagesAvailable = null;

        while (true)
        {
            while (batch.Count < _options.BatchSize
                    && channel.TryRead(out var message))
            {
                batch.Add(message);
            }

            if (batch.Count == _options.BatchSize)
            {
                await PersistAsync(batch, cancellationToken);
                batch.Clear();
                continue;
            }

            messagesAvailable ??= channel.Reader
                .WaitToReadAsync(cancellationToken)
                .AsTask();
            var completed = await Task.WhenAny(messagesAvailable, nextFlush);

            if (completed == nextFlush)
            {
                if (!await nextFlush)
                    break;

                if (batch.Count > 0)
                {
                    await PersistAsync(batch, cancellationToken);
                    batch.Clear();
                }

                nextFlush = flushTimer
                    .WaitForNextTickAsync(cancellationToken)
                    .AsTask();
                continue;
            }

            var canRead = await messagesAvailable;
            messagesAvailable = null;

            if (canRead)
                continue;

            if (batch.Count > 0)
                await PersistAsync(batch, cancellationToken);

            await channel.Reader.Completion;
            return;
        }
    }

    private async Task PersistAsync(
        IReadOnlyCollection<RawMarketMessage> messages,
        CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _inFlightMessageCount, messages.Count);
        await using var scope = scopeFactory.CreateAsyncScope();
        var writer = scope.ServiceProvider
            .GetRequiredService<IRawMarketMessageWriter>();
        await writer.WriteBatchAsync(messages, cancellationToken);
        foreach (var sessionGroup in messages.GroupBy(message => message.SessionId))
        {
            var counters = telemetry.RecordPersisted(
                sessionGroup.Key,
                sessionGroup.LongCount());
            logger.LogInformation(
                "Raw market messages persisted for session {SessionId}. ReceivedComplete: {ReceivedCompleteCount}, Enqueued: {EnqueuedCount}, Persisted: {PersistedCount}.",
                sessionGroup.Key.Value,
                counters.ReceivedComplete,
                counters.Enqueued,
                counters.Persisted);
        }

        Interlocked.Exchange(ref _inFlightMessageCount, 0);
    }

    private int GetUnconfirmedMessageCount()
    {
        return channel.QueuedCount + Volatile.Read(ref _inFlightMessageCount);
    }

    private async Task DisposeCancellationSourceWhenCompletedAsync(Task task)
    {
        await ObserveCompletionAsync(task);
        _drainCts.Dispose();
    }

    private static async Task ObserveCompletionAsync(Task task)
    {
        await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    private static class RawMessagePersistenceErrors
    {
        public static readonly Error PersistenceFailed = new(
            "raw_messages.persistence.failed",
            "Raw market message persistence failed.",
            ErrorType.Failure);

        public static Error DrainTimedOut(TimeSpan timeout) => new(
            "raw_messages.persistence.drain_timeout",
            $"Raw market message persistence did not drain within {timeout}.",
            ErrorType.Failure);
    }
}
