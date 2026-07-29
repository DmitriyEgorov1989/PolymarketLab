using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;

internal sealed class RawMarketMessagePersistenceWorker(
    RawMarketMessageChannel channel,
    IServiceScopeFactory scopeFactory,
    IOptions<RawMessageIngestionOptions> options,
    TimeProvider timeProvider,
    IHostApplicationLifetime applicationLifetime,
    ILogger<RawMarketMessagePersistenceWorker> logger)
    : IHostedService, IDisposable
{
    private readonly RawMessageIngestionOptions _options = options.Value;
    private readonly CancellationTokenSource _persistenceCts = new();
    private Task? _executeTask;
    private int _stopRequested;

    internal Task? ExecuteTask => _executeTask;

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
        if (Interlocked.Exchange(ref _stopRequested, 1) == 0)
        {
            channel.TryComplete();
            _persistenceCts.CancelAfter(_options.ShutdownTimeout);
        }

        using var cancellationRegistration = cancellationToken.Register(
            _persistenceCts.Cancel);

        if (_executeTask is not null)
        {
            var timeoutTask = Task.Delay(
                _options.ShutdownTimeout,
                timeProvider,
                cancellationToken);
            var completed = await Task.WhenAny(_executeTask, timeoutTask);

            if (completed == _executeTask)
            {
                await _executeTask.ConfigureAwait(
                    ConfigureAwaitOptions.SuppressThrowing);
                return;
            }

            _persistenceCts.Cancel();
            _ = ObserveCompletionAsync(_executeTask);
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogWarning(
                "Raw market message persistence exceeded {ShutdownTimeout}; shutdown will continue.",
                _options.ShutdownTimeout);
        }
    }

    public void Dispose()
    {
        channel.TryComplete();
        _persistenceCts.Cancel();

        if (_executeTask is { IsCompleted: false })
            _ = DisposeCancellationSourceWhenCompletedAsync(_executeTask);
        else
            _persistenceCts.Dispose();
    }

    private async Task ExecuteAsync()
    {
        try
        {
            await ConsumeAsync(_persistenceCts.Token);
        }
        catch (OperationCanceledException)
            when (_persistenceCts.IsCancellationRequested)
        {
            logger.LogWarning(
                "Raw market message persistence did not drain within {ShutdownTimeout}.",
                _options.ShutdownTimeout);
        }
        catch (Exception exception)
        {
            channel.TryComplete(exception);
            logger.LogCritical(
                exception,
                "Raw market message persistence failed; stopping the application.");
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
                   && channel.Reader.TryRead(out var message))
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
        await using var scope = scopeFactory.CreateAsyncScope();
        var writer = scope.ServiceProvider
            .GetRequiredService<IRawMarketMessageWriter>();
        await writer.WriteBatchAsync(messages, cancellationToken);
    }

    private async Task DisposeCancellationSourceWhenCompletedAsync(Task task)
    {
        await ObserveCompletionAsync(task);
        _persistenceCts.Dispose();
    }

    private static async Task ObserveCompletionAsync(Task task)
    {
        await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }
}
