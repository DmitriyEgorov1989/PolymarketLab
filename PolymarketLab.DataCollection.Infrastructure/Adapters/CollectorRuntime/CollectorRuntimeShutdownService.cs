using Microsoft.Extensions.Hosting;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;

internal sealed class CollectorRuntimeShutdownService(CollectorRuntime runtime)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return runtime.ShutdownAsync(cancellationToken);
    }
}
