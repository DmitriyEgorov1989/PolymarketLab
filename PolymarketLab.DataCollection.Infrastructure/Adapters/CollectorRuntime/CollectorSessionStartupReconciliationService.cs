using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PolymarketLab.DataCollection.Core.Ports;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;

internal sealed class CollectorSessionStartupReconciliationService(
    IServiceScopeFactory scopeFactory,
    ILogger<CollectorSessionStartupReconciliationService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var reconciler = scope.ServiceProvider
            .GetRequiredService<ICollectorSessionStartupReconciler>();
        var result = await reconciler.ReconcileAsync(cancellationToken);

        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"{result.Error.Code}: {result.Error.Message}");
        }

        logger.LogInformation("Collector session startup reconciliation completed.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
