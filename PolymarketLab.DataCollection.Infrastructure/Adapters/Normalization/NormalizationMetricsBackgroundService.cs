using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Normalization;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;

internal sealed class NormalizationMetricsBackgroundService(
    IServiceScopeFactory scopeFactory,
    NormalizerTelemetry telemetry,
    IOptions<NormalizerOptions> options,
    TimeProvider timeProvider,
    ILogger<NormalizationMetricsBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);
    private readonly NormalizerOptions options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Normalizer backlog metrics refresh failed for projection version {ProjectionVersion}.",
                    options.ProjectionVersion);
            }

            try
            {
                await Task.Delay(RefreshInterval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<INormalizationBacklogReader>();
        var snapshot = await reader.ReadAsync(
            options.ProjectionVersion,
            options.ClaimTimeout,
            cancellationToken);
        telemetry.UpdateBacklog(snapshot);
    }
}
