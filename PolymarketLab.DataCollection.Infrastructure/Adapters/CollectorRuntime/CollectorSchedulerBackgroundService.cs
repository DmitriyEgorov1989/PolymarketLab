using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorScheduling;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using System.Collections.Concurrent;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;

internal sealed class CollectorSchedulerBackgroundService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<CollectorSchedulerBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);
    private readonly ConcurrentDictionary<CollectorSessionId, Task> _operations = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await TickOnceAsync(stoppingToken);

            using var timer = new PeriodicTimer(TickInterval, timeProvider);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await TickOnceAsync(stoppingToken);
        }
        finally
        {
            await WaitForOperationsAsync();
        }
    }

    internal async Task TickOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var scheduler = scope.ServiceProvider.GetRequiredService<ICollectorScheduler>();
            var sessionIds = await scheduler.GetActiveSessionIdsAsync(cancellationToken);
            foreach (var sessionId in sessionIds)
                StartSessionOperation(sessionId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Collector scheduler tick failed unexpectedly; retrying on the next tick.");
        }
    }

    private void StartSessionOperation(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken)
    {
        var reservation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_operations.TryAdd(sessionId, reservation.Task))
            return;

        _ = RunSessionOperationAsync(sessionId, reservation, cancellationToken);
    }

    private async Task RunSessionOperationAsync(
        CollectorSessionId sessionId,
        TaskCompletionSource reservation,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var scheduler = scope.ServiceProvider.GetRequiredService<ICollectorScheduler>();
            var result = await scheduler.TickSessionAsync(sessionId, cancellationToken);
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Collector scheduler operation for session {SessionId} failed with {ErrorCode}: {ErrorMessage}",
                    sessionId.Value,
                    result.Error.Code,
                    result.Error.Message);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Collector scheduler operation for session {SessionId} failed unexpectedly; retrying on the next tick.",
                sessionId.Value);
        }
        finally
        {
            reservation.TrySetResult();
            _operations.TryRemove(
                new KeyValuePair<CollectorSessionId, Task>(sessionId, reservation.Task));
        }
    }

    private async Task WaitForOperationsAsync()
    {
        var operations = _operations.Values.ToArray();
        if (operations.Length == 0)
            return;

        await Task.WhenAll(operations);
    }
}
