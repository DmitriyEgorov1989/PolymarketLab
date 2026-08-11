using CSharpFunctionalExtensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime.WebSockets;
using PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;
using PolymarketLab.SharedKernel.Errors;
using System.Buffers;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;

internal sealed class CollectorWebSocketWorker(
    CollectorRuntimeStartRequest request,
    ICollectorWebSocketFactory webSocketFactory,
    CollectorWebSocketOptions options,
    IRawMarketMessageSink messageSink,
    RawMarketMessageTelemetry telemetry,
    TimeProvider timeProvider,
    IHostApplicationLifetime applicationLifetime,
    ILogger<CollectorWebSocketWorker> logger)
    : ICollectorWorker
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _receiveCts = new();
    private readonly CancellationTokenSource _enqueueCts = new();
    private readonly TaskCompletionSource<CollectorWorkerCompletion> _completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _startInvoked;
    private bool _connectionStarted;
    private bool _stopRequested;
    private bool _lifetimeDisposed;
    private long? _stopStartedTimestamp;
    private ICollectorWebSocketConnection? _startupConnection;
    private ICollectorWebSocketConnection? _activeConnection;

    public Task<CollectorWorkerCompletion> Completion => _completion.Task;

    public async Task<UnitResult<Error>> StartAsync(
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            _startInvoked = true;

            if (_stopRequested)
                return CompleteImmediateStartupFailure(
                    CollectorRuntimeErrors.StartCancelled(request.SessionId));
        }

        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not "ws" and not "wss")
        {
            return CompleteImmediateStartupFailure(
                CollectorRuntimeErrors.InvalidEndpoint(request.SessionId));
        }

        ICollectorWebSocketConnection? connection = null;
        Error? startupError = null;

        try
        {
            using var startupCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _receiveCts.Token,
                applicationLifetime.ApplicationStopping);
            startupCts.CancelAfter(options.ConnectTimeout);

            connection = webSocketFactory.Create();
            lock (_sync)
            {
                _startupConnection = connection;
            }
            await connection.ConnectAsync(endpoint, startupCts.Token);

            var subscription = JsonSerializer.SerializeToUtf8Bytes(
                new MarketSubscription(
                    request.Market.Tokens
                        .Select(token => token.TokenId.Value)
                        .ToArray(),
                    "market",
                    options.CustomFeatureEnabled));

            await connection.SendTextAsync(subscription, startupCts.Token);
            startupCts.Token.ThrowIfCancellationRequested();

            lock (_sync)
            {
                if (_stopRequested
                    || _receiveCts.IsCancellationRequested
                    || applicationLifetime.ApplicationStopping.IsCancellationRequested)
                {
                    startupError = CollectorRuntimeErrors.StartCancelled(
                        request.SessionId);
                    CancelAndDisposeLifetime();
                    return UnitResult.Failure(startupError);
                }

                _startupConnection = null;
                _activeConnection = connection;
                _connectionStarted = true;
            }

            var ownedConnection = connection;
            connection = null;
            _ = RunConnectionAsync(ownedConnection);

            logger.LogInformation(
                "Collector WebSocket {SessionId} connected for market {MarketId}.",
                request.SessionId.Value,
                request.Market.MarketId.Value);

            return UnitResult.Success<Error>();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            startupError = CollectorRuntimeErrors.StartCancelled(request.SessionId);
            CancelAndDisposeLifetime();
            throw;
        }
        catch (OperationCanceledException)
            when (_receiveCts.IsCancellationRequested
                  || applicationLifetime.ApplicationStopping.IsCancellationRequested)
        {
            startupError = CollectorRuntimeErrors.StartCancelled(request.SessionId);
            CancelAndDisposeLifetime();
            return UnitResult.Failure(startupError);
        }
        catch (OperationCanceledException)
        {
            startupError = CollectorRuntimeErrors.StartTimedOut(
                request.SessionId,
                options.ConnectTimeout);
            CancelAndDisposeLifetime();
            return UnitResult.Failure(startupError);
        }
        catch (Exception exception) when
            (exception is WebSocketException or IOException or InvalidOperationException)
        {
            logger.LogError(
                exception,
                "Collector WebSocket {SessionId} failed during startup.",
                request.SessionId.Value);

            startupError = CollectorRuntimeErrors.StartFailed(request.SessionId);
            CancelAndDisposeLifetime();
            return UnitResult.Failure(startupError);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Collector WebSocket {SessionId} failed unexpectedly during startup.",
                request.SessionId.Value);
            startupError = CollectorRuntimeErrors.StartFailed(request.SessionId);
            CancelAndDisposeLifetime();
            return UnitResult.Failure(startupError);
        }
        finally
        {
            try
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_startupConnection, connection))
                        _startupConnection = null;
                }

                connection?.Dispose();
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Collector WebSocket {SessionId} failed to dispose after startup.",
                    request.SessionId.Value);
                startupError ??= CollectorRuntimeErrors.StartFailed(
                    request.SessionId);
            }
            finally
            {
                if (startupError is not null)
                {
                    _completion.TrySetResult(new CollectorWorkerCompletion(
                        UnitResult.Failure(startupError),
                        CollectorWorkerCompletionOrigin.Startup,
                        timeProvider.GetUtcNow()));
                }
            }
        }
    }

    public async Task<UnitResult<Error>> StopAsync(
        CancellationToken cancellationToken)
    {
        bool startInvoked;
        bool connectionStarted;

        lock (_sync)
        {
            if (!_stopRequested)
                _stopStartedTimestamp = timeProvider.GetTimestamp();

            _stopRequested = true;
            startInvoked = _startInvoked;
            connectionStarted = _connectionStarted;

            if (!_lifetimeDisposed)
            {
                _receiveCts.Cancel();
                _enqueueCts.CancelAfter(options.StopTimeout);
            }
        }

        if (!startInvoked)
        {
            _completion.TrySetResult(new CollectorWorkerCompletion(
                UnitResult.Success<Error>(),
                GetRequestedCompletionOrigin(),
                timeProvider.GetUtcNow()));
            return UnitResult.Success<Error>();
        }

        if (!connectionStarted)
        {
            var completionResult = await WaitForStopCompletionAsync(
                cancellationToken);
            return completionResult.IsFailure
                   && completionResult.Error.Code ==
                   "collector.runtime.stop.timeout"
                ? completionResult
                : UnitResult.Success<Error>();
        }

        return await WaitForStopCompletionAsync(cancellationToken);
    }

    private async Task RunConnectionAsync(
        ICollectorWebSocketConnection connection)
    {
        UnitResult<Error> result;

        try
        {
            result = await ReceiveLoopAsync(connection);
        }
        catch (RawMessageEnqueueCancelledException exception)
        {
            var counters = telemetry.GetSnapshot(request.SessionId);
            logger.LogError(
                exception,
                "Collector WebSocket {SessionId} enqueue was cancelled after message completion. ReceivedComplete: {ReceivedCompleteCount}, Enqueued: {EnqueuedCount}, Persisted: {PersistedCount}.",
                request.SessionId.Value,
                counters.ReceivedComplete,
                counters.Enqueued,
                counters.Persisted);
            result = UnitResult.Failure(
                CollectorRuntimeErrors.EnqueueCancelled(request.SessionId));
        }
        catch (OperationCanceledException)
            when (_receiveCts.IsCancellationRequested)
        {
            result = UnitResult.Success<Error>();
        }
        catch (ChannelClosedException exception)
        {
            var counters = telemetry.GetSnapshot(request.SessionId);
            logger.LogError(
                exception,
                "Collector WebSocket {SessionId} cannot enqueue received messages. ReceivedComplete: {ReceivedCompleteCount}, Enqueued: {EnqueuedCount}, Persisted: {PersistedCount}.",
                request.SessionId.Value,
                counters.ReceivedComplete,
                counters.Enqueued,
                counters.Persisted);
            result = UnitResult.Failure(
                CollectorRuntimeErrors.IngestionClosed(request.SessionId));
        }
        catch (Exception exception) when
            (exception is WebSocketException or IOException or InvalidOperationException)
        {
            if (IsStopping())
            {
                result = UnitResult.Success<Error>();
            }
            else
            {
                logger.LogError(
                    exception,
                    "Collector WebSocket {SessionId} failed while receiving messages.",
                    request.SessionId.Value);
                result = UnitResult.Failure(
                    CollectorRuntimeErrors.ReceiveFailed(request.SessionId));
            }
        }
        catch (Exception exception)
        {
            if (IsStopping())
            {
                result = UnitResult.Success<Error>();
            }
            else
            {
                logger.LogError(
                    exception,
                    "Collector WebSocket {SessionId} failed unexpectedly.",
                    request.SessionId.Value);
                result = UnitResult.Failure(
                    CollectorRuntimeErrors.ReceiveFailed(request.SessionId));
            }
        }

        var completionOrigin = result.IsFailure
            ? CollectorWorkerCompletionOrigin.Autonomous
            : GetRequestedCompletionOrigin();
        var completedAt = timeProvider.GetUtcNow();

        try
        {
            var closeResult = await CloseConnectionAsync(connection);
            if (result.IsSuccess && closeResult.IsFailure)
                result = closeResult;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Collector WebSocket {SessionId} failed unexpectedly during cleanup.",
                request.SessionId.Value);
            if (result.IsSuccess)
            {
                result = UnitResult.Failure(
                    CollectorRuntimeErrors.StopFailed(request.SessionId));
            }
        }
        finally
        {
            try
            {
                connection.Dispose();
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Collector WebSocket {SessionId} failed to dispose.",
                    request.SessionId.Value);
                if (result.IsSuccess)
                {
                    result = UnitResult.Failure(
                        CollectorRuntimeErrors.StopFailed(request.SessionId));
                }
            }

            lock (_sync)
            {
                if (ReferenceEquals(_activeConnection, connection))
                    _activeConnection = null;
            }

            DisposeLifetime();

            if (result.IsFailure)
            {
                var counters = telemetry.GetSnapshot(request.SessionId);
                logger.LogError(
                    "Collector WebSocket {SessionId} completed with error {ErrorCode}. ReceivedComplete: {ReceivedCompleteCount}, Enqueued: {EnqueuedCount}, Persisted: {PersistedCount}.",
                    request.SessionId.Value,
                    result.Error.Code,
                    counters.ReceivedComplete,
                    counters.Enqueued,
                    counters.Persisted);
            }

            _completion.TrySetResult(new CollectorWorkerCompletion(
                result,
                completionOrigin,
                completedAt));
        }
    }

    private async Task<UnitResult<Error>> ReceiveLoopAsync(
        ICollectorWebSocketConnection connection)
    {
        var frameBuffer = ArrayPool<byte>.Shared.Rent(options.ReceiveBufferSize);
        var messageBuffer = new ArrayBufferWriter<byte>(options.ReceiveBufferSize);

        try
        {
            while (true)
            {
                var frame = await connection.ReceiveAsync(
                    frameBuffer.AsMemory(0, options.ReceiveBufferSize),
                    _receiveCts.Token);

                if (frame.MessageType == WebSocketMessageType.Close)
                {
                    return IsStopping()
                        ? UnitResult.Success<Error>()
                        : UnitResult.Failure(
                            CollectorRuntimeErrors.RemoteClosed(request.SessionId));
                }

                if (frame.MessageType != WebSocketMessageType.Text)
                {
                    return UnitResult.Failure(
                        CollectorRuntimeErrors.UnsupportedMessageType(
                            request.SessionId,
                            frame.MessageType));
                }

                if (messageBuffer.WrittenCount >
                    options.MaximumMessageSize - frame.Count)
                {
                    return UnitResult.Failure(
                        CollectorRuntimeErrors.MessageTooLarge(
                            request.SessionId,
                            options.MaximumMessageSize));
                }

                messageBuffer.Write(frameBuffer.AsSpan(0, frame.Count));

                if (!frame.EndOfMessage)
                    continue;

                var message = new RawMarketMessage(
                    request.SessionId,
                    timeProvider.GetUtcNow(),
                    messageBuffer.WrittenSpan.ToArray());
                var receivedCounters = telemetry.RecordReceivedComplete(
                    request.SessionId,
                    message.ReceivedAt);
                logger.LogDebug(
                    "Collector WebSocket {SessionId} received complete message. ReceivedComplete: {ReceivedCompleteCount}, Enqueued: {EnqueuedCount}, Persisted: {PersistedCount}.",
                    request.SessionId.Value,
                    receivedCounters.ReceivedComplete,
                    receivedCounters.Enqueued,
                    receivedCounters.Persisted);

                try
                {
                    await messageSink.EnqueueAsync(message, _enqueueCts.Token);
                }
                catch (OperationCanceledException exception)
                    when (_enqueueCts.IsCancellationRequested)
                {
                    throw new RawMessageEnqueueCancelledException(exception);
                }

                var enqueuedCounters = telemetry.RecordEnqueued(request.SessionId);
                logger.LogDebug(
                    "Collector WebSocket {SessionId} enqueued raw message. ReceivedComplete: {ReceivedCompleteCount}, Enqueued: {EnqueuedCount}, Persisted: {PersistedCount}.",
                    request.SessionId.Value,
                    enqueuedCounters.ReceivedComplete,
                    enqueuedCounters.Enqueued,
                    enqueuedCounters.Persisted);
                messageBuffer.Clear();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(frameBuffer);
        }
    }

    private async Task<UnitResult<Error>> CloseConnectionAsync(
        ICollectorWebSocketConnection connection)
    {
        try
        {
            var remainingTimeout = GetRemainingStopTimeout();
            if (remainingTimeout <= TimeSpan.Zero)
            {
                return UnitResult.Failure(
                    CollectorRuntimeErrors.StopTimedOut(
                        request.SessionId,
                        options.StopTimeout));
            }

            using var shutdownCts = new CancellationTokenSource(remainingTimeout);
            await connection.CloseAsync(shutdownCts.Token);
            return UnitResult.Success<Error>();
        }
        catch (OperationCanceledException)
        {
            logger.LogError(
                "Collector WebSocket {SessionId} timed out during shutdown.",
                request.SessionId.Value);
            return UnitResult.Failure(
                CollectorRuntimeErrors.StopTimedOut(
                    request.SessionId,
                    options.StopTimeout));
        }
        catch (Exception exception) when
            (exception is WebSocketException or IOException or InvalidOperationException)
        {
            logger.LogError(
                exception,
                "Collector WebSocket {SessionId} failed during shutdown.",
                request.SessionId.Value);
            return UnitResult.Failure(
                CollectorRuntimeErrors.StopFailed(request.SessionId));
        }
    }

    private bool IsStopping()
    {
        lock (_sync)
        {
            return _stopRequested || _receiveCts.IsCancellationRequested;
        }
    }

    private TimeSpan GetRemainingStopTimeout()
    {
        lock (_sync)
        {
            if (_stopStartedTimestamp is null)
                return options.StopTimeout;

            var elapsed = timeProvider.GetElapsedTime(
                _stopStartedTimestamp.Value,
                timeProvider.GetTimestamp());
            return options.StopTimeout - elapsed;
        }
    }

    private async Task<UnitResult<Error>> WaitForStopCompletionAsync(
        CancellationToken cancellationToken)
    {
        var remainingTimeout = GetRemainingStopTimeout();
        if (remainingTimeout <= TimeSpan.Zero)
            return AbortTimedOutConnection();

        try
        {
            var completion = await Completion.WaitAsync(
                remainingTimeout,
                timeProvider,
                cancellationToken);
            return completion.Result;
        }
        catch (TimeoutException)
        {
            return AbortTimedOutConnection();
        }
    }

    private UnitResult<Error> AbortTimedOutConnection()
    {
        ICollectorWebSocketConnection? connection;

        lock (_sync)
        {
            connection = _activeConnection ?? _startupConnection;
        }

        try
        {
            connection?.Dispose();
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Collector WebSocket {SessionId} failed to abort after timeout.",
                request.SessionId.Value);
        }

        logger.LogError(
            "Collector WebSocket {SessionId} did not stop within {StopTimeout}.",
            request.SessionId.Value,
            options.StopTimeout);
        return UnitResult.Failure(
            CollectorRuntimeErrors.StopTimedOut(
                request.SessionId,
                options.StopTimeout));
    }

    private UnitResult<Error> CompleteImmediateStartupFailure(Error error)
    {
        CancelAndDisposeLifetime();
        var result = UnitResult.Failure(error);
        _completion.TrySetResult(new CollectorWorkerCompletion(
            result,
            CollectorWorkerCompletionOrigin.Startup,
            timeProvider.GetUtcNow()));
        return result;
    }

    private CollectorWorkerCompletionOrigin GetRequestedCompletionOrigin()
    {
        return applicationLifetime.ApplicationStopping.IsCancellationRequested
            ? CollectorWorkerCompletionOrigin.ApplicationShutdown
            : CollectorWorkerCompletionOrigin.RequestedStop;
    }

    private void CancelAndDisposeLifetime()
    {
        lock (_sync)
        {
            if (_lifetimeDisposed)
                return;

            _receiveCts.Cancel();
            _enqueueCts.Cancel();
            _receiveCts.Dispose();
            _enqueueCts.Dispose();
            _lifetimeDisposed = true;
        }
    }

    private void DisposeLifetime()
    {
        lock (_sync)
        {
            if (_lifetimeDisposed)
                return;

            _receiveCts.Dispose();
            _enqueueCts.Dispose();
            _lifetimeDisposed = true;
        }
    }

    private sealed record MarketSubscription(
        [property: JsonPropertyName("assets_ids")] string[] AssetIds,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("custom_feature_enabled")] bool CustomFeatureEnabled);

    private sealed class RawMessageEnqueueCancelledException(
        Exception innerException)
        : Exception("Raw message enqueue was cancelled.", innerException);
}
