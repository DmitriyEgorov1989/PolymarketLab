using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorRuntimeFailure;
using PolymarketLab.DataCollection.Core.Ports.Dtos;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;

internal sealed class CollectorRuntimeFailureDispatcher(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime applicationLifetime,
    ILogger<CollectorRuntimeFailureDispatcher> logger)
    : ICollectorRuntimeFailureDispatcher
{
    public async Task<bool> DispatchAsync(
        CollectorRuntimeFailure failure,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider
                .GetRequiredService<ICollectorRuntimeFailureHandler>();
            var result = await handler.HandleAsync(failure, cancellationToken);

            if (result.IsSuccess)
                return true;

            logger.LogCritical(
                "Collector runtime failure for session {SessionId} could not be persisted: {ErrorCode}.",
                failure.SessionId.Value,
                result.Error.Code);
        }
        catch (Exception exception)
        {
            logger.LogCritical(
                exception,
                "Collector runtime failure for session {SessionId} could not be persisted.",
                failure.SessionId.Value);
        }

        applicationLifetime.StopApplication();
        return false;
    }
}
