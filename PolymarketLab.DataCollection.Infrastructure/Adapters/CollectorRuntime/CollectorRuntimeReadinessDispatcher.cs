using CSharpFunctionalExtensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorRuntimeReadiness;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;

internal sealed class CollectorRuntimeReadinessDispatcher(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime applicationLifetime,
    ILogger<CollectorRuntimeReadinessDispatcher> logger)
    : ICollectorRuntimeReadinessDispatcher
{
    public Task<UnitResult<Error>> MarkAwaitingInitialBooksAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken) =>
        DispatchAsync(
            sessionId,
            handler => handler.MarkAwaitingInitialBooksAsync(sessionId, cancellationToken),
            cancellationToken);

    public Task<UnitResult<Error>> MarkAwaitingHeartbeatAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken) =>
        DispatchAsync(
            sessionId,
            handler => handler.MarkAwaitingHeartbeatAsync(sessionId, cancellationToken),
            cancellationToken);

    public Task<UnitResult<Error>> MarkRunningAsync(
        CollectorSessionId sessionId,
        DateTimeOffset subscriptionReadyAt,
        CancellationToken cancellationToken) =>
        DispatchAsync(
            sessionId,
            handler => handler.MarkRunningAsync(
                sessionId,
                subscriptionReadyAt,
                cancellationToken),
            cancellationToken);

    public Task<UnitResult<Error>> BeginInvalidationAsync(
        CollectorSessionId sessionId,
        Error failure,
        CancellationToken cancellationToken) =>
        DispatchAsync(
            sessionId,
            handler => handler.BeginInvalidationAsync(
                sessionId,
                failure,
                cancellationToken),
            cancellationToken);

    public Task<UnitResult<Error>> RecordInitialBookEnqueuedAsync(
        CollectorSessionId sessionId,
        TokenId tokenId,
        long connectionEpoch,
        DateTimeOffset enqueuedAt,
        CancellationToken cancellationToken) =>
        DispatchAsync(
            sessionId,
            handler => handler.RecordInitialBookEnqueuedAsync(
                sessionId,
                tokenId,
                connectionEpoch,
                enqueuedAt,
                cancellationToken),
            cancellationToken);

    private async Task<UnitResult<Error>> DispatchAsync(
        CollectorSessionId sessionId,
        Func<ICollectorRuntimeReadinessHandler, Task<UnitResult<Error>>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider
                .GetRequiredService<ICollectorRuntimeReadinessHandler>();
            var result = await action(handler);

            if (result.IsSuccess)
                return result;

            logger.LogCritical(
                "Collector runtime readiness update for session {SessionId} failed: {ErrorCode}.",
                sessionId.Value,
                result.Error.Code);
            applicationLifetime.StopApplication();
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogCritical(
                exception,
                "Collector runtime readiness update for session {SessionId} failed unexpectedly.",
                sessionId.Value);
            applicationLifetime.StopApplication();
            return UnitResult.Failure(
                CollectorRuntimeErrors.ReadinessPersistenceFailed(sessionId));
        }
    }
}
