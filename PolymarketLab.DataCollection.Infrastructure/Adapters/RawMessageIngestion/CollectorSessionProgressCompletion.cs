using CSharpFunctionalExtensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;

internal sealed class CollectorSessionProgressCompletion(
    RawMarketMessageTelemetry telemetry,
    IServiceScopeFactory scopeFactory,
    IOptions<RawMessageIngestionOptions> options,
    ILogger<CollectorSessionProgressCompletion> logger)
    : ICollectorSessionProgressCompletion
{
    private readonly TimeSpan _timeout = options.Value.ShutdownTimeout;

    public async Task<UnitResult<Error>> CompleteAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken)
    {
        var finalEnqueued = telemetry.GetSnapshot(sessionId).Enqueued;
        using var timeoutCts = new CancellationTokenSource(_timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);

        try
        {
            await telemetry.WaitUntilPersistedAsync(
                sessionId,
                finalEnqueued,
                linkedCts.Token);

            await using var scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider
                .GetRequiredService<ICollectorSessionProgressRepository>();
            var finalCheckpoint = telemetry.GetCheckpoint(sessionId);
            await repository.CheckpointAsync(
                finalCheckpoint,
                linkedCts.Token);

            return UnitResult.Success<Error>();
        }
        catch (OperationCanceledException) when (
            timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return UnitResult.Failure(new Error(
                "collector.progress.persistence_timeout",
                $"Collector session progress did not persist within {_timeout}.",
                ErrorType.Failure));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Failed to checkpoint collector session progress for {SessionId}.",
                sessionId.Value);
            return UnitResult.Failure(new Error(
                "collector.progress.persistence_failed",
                "Collector session progress could not be persisted.",
                ErrorType.Failure));
        }
    }
}
