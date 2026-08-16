using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Application.Normalization;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;

internal sealed class NormalizationBackgroundService(
    IServiceScopeFactory scopeFactory,
    NormalizerTelemetry telemetry,
    IOptions<NormalizerOptions> options,
    TimeProvider timeProvider,
    ILogger<NormalizationBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(30);
    private readonly NormalizerOptions options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Normalizer background service is disabled.");
            return;
        }

        var consecutiveFailures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var startedAt = timeProvider.GetTimestamp();
                using var batchCancellation = new CancellationTokenSource();
                using var stopRegistration = stoppingToken.Register(
                    () => batchCancellation.CancelAfter(options.ShutdownTimeout));
                if (stoppingToken.IsCancellationRequested)
                    return;

                var result = await ProcessBatchAsync(batchCancellation.Token);
                var duration = timeProvider.GetElapsedTime(startedAt);
                telemetry.RecordBatch(options.ProjectionVersion, result, duration);
                LogBatch(result, duration);
                LogMessageErrors(result.Errors);
                consecutiveFailures = 0;
                if (stoppingToken.IsCancellationRequested)
                    return;

                if (result.Total == 0)
                    await Task.Delay(options.IdleDelay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                consecutiveFailures++;
                var retryDelay = GetRetryDelay(consecutiveFailures);
                logger.LogError(
                    exception,
                    "Normalizer background iteration failed. Retrying after {RetryDelay}.",
                    retryDelay);

                try
                {
                    await Task.Delay(retryDelay, timeProvider, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    private async Task<NormalizationBatchResult> ProcessBatchAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<INormalizationProcessor>();
        return await processor.ProcessBatchAsync(cancellationToken);
    }

    private static TimeSpan GetRetryDelay(int consecutiveFailures)
    {
        var exponent = Math.Min(consecutiveFailures - 1, 5);
        var delay = TimeSpan.FromTicks(InitialRetryDelay.Ticks * (1L << exponent));
        return delay <= MaximumRetryDelay ? delay : MaximumRetryDelay;
    }

    private void LogBatch(NormalizationBatchResult result, TimeSpan duration)
    {
        if (result.Total == 0)
            return;

        logger.LogInformation(
            "Normalizer batch completed. ProjectionVersion: {ProjectionVersion}, BatchSize: {BatchSize}, FirstRawMessageId: {FirstRawMessageId}, LastRawMessageId: {LastRawMessageId}, Processed: {Processed}, Invalid: {Invalid}, Unsupported: {Unsupported}, Failed: {Failed}, DurationMs: {DurationMs}.",
            options.ProjectionVersion,
            result.Total,
            result.FirstRawMessageId,
            result.LastRawMessageId,
            result.Processed,
            result.Invalid,
            result.Unsupported,
            result.Failed,
            duration.TotalMilliseconds);
    }

    private void LogMessageErrors(IReadOnlyCollection<NormalizationMessageError> errors)
    {
        foreach (var error in errors)
        {
            logger.Log(
                error.Status == NormalizationStatus.Failed
                    ? LogLevel.Error
                    : LogLevel.Warning,
                default,
                error.Exception,
                "Normalizer message failed. RawMessageId: {RawMessageId}, SessionId: {SessionId}, RawItemIndex: {RawItemIndex}, EventType: {EventType}, ProjectionVersion: {ProjectionVersion}, NormalizerVersion: {NormalizerVersion}, ErrorCode: {ErrorCode}, ErrorField: {ErrorField}.",
                error.RawMessageId,
                error.SessionId.Value,
                error.RawItemIndex,
                error.EventType,
                error.ProjectionVersion,
                error.NormalizerVersion,
                error.ErrorCode,
                error.ErrorField);
        }
    }
}
