using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorScheduling;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;

internal sealed class CollectorSchedulerBackgroundService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<CollectorSchedulerBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await TickOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(TickInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await TickOnceAsync(stoppingToken);
    }

    internal async Task TickOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<ICollectorScheduler>();
        var result = await scheduler.TickAsync(cancellationToken);
        if (result.IsFailure)
        {
            logger.LogWarning(
                "Collector scheduler tick failed with {ErrorCode}: {ErrorMessage}",
                result.Error.Code,
                result.Error.Message);
        }
    }
}
