using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PolymarketLab.DataCollection.Core.Application.UseCases.ResolutionConsensus;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Resolution;

internal sealed class ResolutionConsensusBackgroundService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ResolutionConsensusBackgroundService> logger) : BackgroundService
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
        var coordinator = scope.ServiceProvider
            .GetRequiredService<IResolutionConsensusCoordinator>();
        var result = await coordinator.TickAsync(cancellationToken);
        if (result.IsFailure)
        {
            logger.LogWarning(
                "Resolution consensus tick failed with {ErrorCode}: {ErrorMessage}",
                result.Error.Code,
                result.Error.Message);
        }
    }
}
