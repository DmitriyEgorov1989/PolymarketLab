using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorRawDatasetCompletion;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;

internal sealed class CollectorRuntimeWindowCompletionDispatcher(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime applicationLifetime,
    ILogger<CollectorRuntimeWindowCompletionDispatcher> logger)
    : ICollectorRuntimeWindowCompletionDispatcher
{
    public async Task DispatchAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var coordinator = scope.ServiceProvider
                .GetRequiredService<ICollectorRawDatasetCompletionCoordinator>();
            var result = await coordinator.CompleteAsync(sessionId, cancellationToken);
            if (result.IsSuccess)
                return;

            logger.LogError(
                "Collector window completion for session {SessionId} failed: {ErrorCode}.",
                sessionId.Value,
                result.Error.Code);

            var repository = scope.ServiceProvider
                .GetRequiredService<ICollectorSessionRepository>();
            var session = await repository.GetByIdAsync(sessionId, cancellationToken);
            if (session?.Status is CollectorSessionStatus.Invalidating
                or CollectorSessionStatus.Failed
                || session?.Status == CollectorSessionStatus.Stopped
                && session.StopReason == CollectorStopReason.MarketClosed)
            {
                return;
            }

            logger.LogCritical(
                "Collector window completion for session {SessionId} failed without durable invalidation.",
                sessionId.Value);
            applicationLifetime.StopApplication();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogCritical(
                exception,
                "Collector window completion for session {SessionId} failed unexpectedly.",
                sessionId.Value);
            applicationLifetime.StopApplication();
        }
    }
}
