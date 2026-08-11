using CSharpFunctionalExtensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionShutdown;
using PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;

internal sealed class CollectorRuntimeShutdownService(
    CollectorRuntime runtime,
    IRawMessagePersistenceCompletion rawMessagePersistenceCompletion,
    IServiceScopeFactory scopeFactory,
    IOptions<CollectorLifecycleOptions> options,
    ILogger<CollectorRuntimeShutdownService> logger)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var shutdownEntries = runtime.BeginShutdown();
        var sessionIds = shutdownEntries
            .Select(entry => entry.SessionId)
            .ToArray();
        await UpdatePersistedStateAsync(
            sessionIds,
            (handler, sessionId, token) =>
                handler.MarkStoppingAsync(sessionId, token),
            "mark collector sessions as stopping");

        IReadOnlyCollection<CollectorRuntimeShutdownResult> shutdownResults;
        using var runtimeShutdownCts = new CancellationTokenSource(
            options.Value.ShutdownTimeout);
        try
        {
            shutdownResults = await runtime.ShutdownAsync(
                shutdownEntries,
                runtimeShutdownCts.Token);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Collector runtime shutdown did not complete before the host deadline.");
            return;
        }

        foreach (var shutdownResult in shutdownResults.Where(result => result.Result.IsFailure))
        {
            logger.LogError(
                shutdownResult.Exception,
                "Collector session {SessionId} failed to stop: {ErrorCode}.",
                shutdownResult.SessionId.Value,
                shutdownResult.Result.Error.Code);
        }

        var stoppedSessionIds = shutdownResults
            .Where(result => result.Result.IsSuccess)
            .Select(result => result.SessionId)
            .ToArray();

        rawMessagePersistenceCompletion.CompleteProducers();
        RawMessagePersistenceCompletionResult persistenceCompletion;
        using var persistenceCompletionCts = new CancellationTokenSource(
            options.Value.ShutdownTimeout);
        try
        {
            persistenceCompletion = await rawMessagePersistenceCompletion
                .WaitForCompletionAsync(persistenceCompletionCts.Token);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Raw market message persistence completion failed during collector shutdown.");
            persistenceCompletion = RawMessagePersistenceCompletionResult.Failure(
                new Error(
                    "raw_messages.persistence.completion_failed",
                    "Raw market message persistence completion failed.",
                    ErrorType.Failure),
                null);
        }

        if (persistenceCompletion.Result.IsFailure)
        {
            logger.LogError(
                "Raw market message persistence did not complete successfully during collector shutdown: {ErrorCode}. Unconfirmed messages: {UnconfirmedMessageCount}.",
                persistenceCompletion.Result.Error.Code,
                persistenceCompletion.UnconfirmedMessageCount);

            await UpdatePersistedStateAsync(
                stoppedSessionIds,
                (handler, sessionId, token) => handler.MarkFailedAsync(
                    sessionId,
                    persistenceCompletion.Result.Error,
                    token),
                "mark collector sessions as failed after raw message persistence failure");
            return;
        }

        await UpdatePersistedStateAsync(
            stoppedSessionIds,
            (handler, sessionId, token) =>
                handler.MarkStoppedAsync(sessionId, token),
            "mark collector sessions as stopped");
    }

    private async Task UpdatePersistedStateAsync(
        IReadOnlyCollection<CollectorSessionId> sessionIds,
        Func<ICollectorSessionShutdownHandler,
            CollectorSessionId,
            CancellationToken,
            Task<UnitResult<Error>>> update,
        string operation)
    {
        using var persistenceCts = new CancellationTokenSource(
            options.Value.ShutdownTimeout);

        foreach (var sessionId in sessionIds)
        {
            if (persistenceCts.IsCancellationRequested)
            {
                logger.LogError(
                    "Could not {Operation} before the configured deadline.",
                    operation);
                return;
            }

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider
                    .GetRequiredService<ICollectorSessionShutdownHandler>();
                var result = await update(
                    handler,
                    sessionId,
                    persistenceCts.Token);
                if (result.IsFailure)
                {
                    logger.LogError(
                        "Could not {Operation} for session {SessionId}: {ErrorCode}.",
                        operation,
                        sessionId.Value,
                        result.Error.Code);
                }
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Could not {Operation} for session {SessionId}.",
                    operation,
                    sessionId.Value);
            }
        }
    }
}
