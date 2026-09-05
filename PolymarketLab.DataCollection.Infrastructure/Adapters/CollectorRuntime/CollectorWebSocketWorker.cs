using CSharpFunctionalExtensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime.WebSockets;
using PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;
using PolymarketLab.SharedKernel.DomainModels.Ids;
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
    ICollectorRuntimeReadinessDispatcher readinessDispatcher,
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
            if (CanRetryStartup())
            {
                startupError = null;
                return StartBackgroundReconnectAfterStartupFailure();
            }

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
            if (CanRetryStartup())
            {
                startupError = null;
                return StartBackgroundReconnectAfterStartupFailure();
            }

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
            if (CanRetryStartup())
            {
                startupError = null;
                return StartBackgroundReconnectAfterStartupFailure();
            }

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
        var epoch = 1L;
        var state = new ConnectionReadinessState(
            epoch,
            request.Market.ConditionId,
            request.Market.Tokens
                .Select(token => token.TokenId.Value)
                .ToArray());
        telemetry.RecordConnectionEpoch(request.SessionId, state.Epoch);

        await RunConnectionAttemptsAsync(connection, state);
    }

    private bool CanRetryStartup() =>
        !IsStopping()
        && !applicationLifetime.ApplicationStopping.IsCancellationRequested
        && timeProvider.GetUtcNow() < request.ReadinessDeadline;

    private UnitResult<Error> StartBackgroundReconnectAfterStartupFailure()
    {
        lock (_sync)
        {
            _startupConnection = null;
            _connectionStarted = true;
        }

        _ = RunStartupReconnectAsync();
        return UnitResult.Success<Error>();
    }

    private async Task RunStartupReconnectAsync()
    {
        var state = new ConnectionReadinessState(
            1,
            request.Market.ConditionId,
            request.Market.Tokens
                .Select(token => token.TokenId.Value)
                .ToArray());

        while (!IsStopping() && timeProvider.GetUtcNow() < request.ReadinessDeadline)
        {
            var delay = request.ReadinessDeadline - timeProvider.GetUtcNow();
            if (delay > options.ReconnectDelay)
                delay = options.ReconnectDelay;

            try
            {
                await Task.Delay(delay, timeProvider, _receiveCts.Token);
            }
            catch (OperationCanceledException) when (_receiveCts.IsCancellationRequested)
            {
                _completion.TrySetResult(new CollectorWorkerCompletion(
                    UnitResult.Success<Error>(),
                    GetRequestedCompletionOrigin(),
                    timeProvider.GetUtcNow()));
                return;
            }

            state = state.NextEpoch();
            var reconnect = await ConnectAndSubscribeAsync(
                state.Epoch,
                _receiveCts.Token);
            if (reconnect.IsFailure)
                continue;

            telemetry.RecordConnectionEpoch(request.SessionId, state.Epoch);
            telemetry.RecordReconnect(request.SessionId);
            await RunConnectionAttemptsAsync(reconnect.Value, state);
            return;
        }

        var error = CollectorRuntimeErrors.ReadinessTimedOut(request.SessionId);
        await readinessDispatcher.BeginInvalidationAsync(
            request.SessionId,
            error,
            CancellationToken.None);
        _completion.TrySetResult(new CollectorWorkerCompletion(
            UnitResult.Failure(error),
            CollectorWorkerCompletionOrigin.Invalidated,
            timeProvider.GetUtcNow()));
    }

    private async Task RunConnectionAttemptsAsync(
        ICollectorWebSocketConnection connection,
        ConnectionReadinessState state)
    {
        UnitResult<Error> result = UnitResult.Success<Error>();
        var completionOrigin = CollectorWorkerCompletionOrigin.RequestedStop;

        while (true)
        {
            result = await RunSingleConnectionAsync(connection, state);

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
            }

            if (result.IsSuccess)
            {
                completionOrigin = GetRequestedCompletionOrigin();
                break;
            }

            if (IsStopping() || applicationLifetime.ApplicationStopping.IsCancellationRequested)
            {
                completionOrigin = CollectorWorkerCompletionOrigin.Autonomous;
                break;
            }

            if (state.IsReady || timeProvider.GetUtcNow() >= request.ReadinessDeadline)
            {
                await readinessDispatcher.BeginInvalidationAsync(
                    request.SessionId,
                    result.Error,
                    CancellationToken.None);
                completionOrigin = CollectorWorkerCompletionOrigin.Invalidated;
                break;
            }

            var reconnectDelay = request.ReadinessDeadline - timeProvider.GetUtcNow();
            if (reconnectDelay > options.ReconnectDelay)
                reconnectDelay = options.ReconnectDelay;

            try
            {
                await Task.Delay(reconnectDelay, timeProvider, _receiveCts.Token);
            }
            catch (OperationCanceledException) when (_receiveCts.IsCancellationRequested)
            {
                result = UnitResult.Success<Error>();
                completionOrigin = GetRequestedCompletionOrigin();
                break;
            }

            state = state.NextEpoch();
            var reconnect = await ConnectAndSubscribeAsync(
                state.Epoch,
                _receiveCts.Token);
            if (reconnect.IsFailure)
            {
                result = UnitResult.Failure(reconnect.Error);
                if (timeProvider.GetUtcNow() < request.ReadinessDeadline)
                    continue;

                await readinessDispatcher.BeginInvalidationAsync(
                    request.SessionId,
                    result.Error,
                    CancellationToken.None);
                completionOrigin = CollectorWorkerCompletionOrigin.Invalidated;
                break;
            }

            telemetry.RecordConnectionEpoch(request.SessionId, state.Epoch);
            telemetry.RecordReconnect(request.SessionId);
            connection = reconnect.Value;
        }

        var completedAt = timeProvider.GetUtcNow();

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

    private async Task<UnitResult<Error>> RunSingleConnectionAsync(
        ICollectorWebSocketConnection connection,
        ConnectionReadinessState state)
    {
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(
            _receiveCts.Token,
            applicationLifetime.ApplicationStopping);
        try
        {
            var readinessResult = await readinessDispatcher.MarkAwaitingInitialBooksAsync(
                request.SessionId,
                heartbeatCts.Token);
            if (readinessResult.IsFailure)
                return readinessResult;

            var initialPingAt = timeProvider.GetTimestamp();
            await connection.SendTextAsync("PING"u8.ToArray(), heartbeatCts.Token);
            state.ObservePingSent(initialPingAt);
        }
        catch (OperationCanceledException) when (heartbeatCts.IsCancellationRequested)
        {
            return UnitResult.Success<Error>();
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Collector WebSocket {SessionId} failed while initializing readiness or sending initial heartbeat.",
                request.SessionId.Value);
            return UnitResult.Failure(
                CollectorRuntimeErrors.ReceiveFailed(request.SessionId));
        }

        var heartbeat = HeartbeatLoopAsync(connection, state, heartbeatCts);
        var receive = ReceiveLoopAsync(connection, state, heartbeatCts.Token);

        var completed = await Task.WhenAny(receive, heartbeat);
        heartbeatCts.Cancel();

        await ((Task)Task.WhenAll(receive, heartbeat))
            .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        var receiveResult = await receive;
        if (receiveResult.IsFailure)
            return receiveResult;

        if (completed == heartbeat)
            return await heartbeat;

        return receiveResult;
    }

    private async Task<UnitResult<Error>> ReceiveLoopAsync(
        ICollectorWebSocketConnection connection,
        ConnectionReadinessState state,
        CancellationToken receiveToken)
    {
        try
        {
            return await ReceiveLoopCoreAsync(connection, state, receiveToken);
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
            return UnitResult.Failure(
                CollectorRuntimeErrors.EnqueueCancelled(request.SessionId));
        }
        catch (OperationCanceledException)
            when (_receiveCts.IsCancellationRequested || receiveToken.IsCancellationRequested)
        {
            return UnitResult.Success<Error>();
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
            return UnitResult.Failure(
                CollectorRuntimeErrors.IngestionClosed(request.SessionId));
        }
        catch (Exception exception) when
            (exception is WebSocketException or IOException or InvalidOperationException)
        {
            if (IsStopping())
                return UnitResult.Success<Error>();

            logger.LogError(
                exception,
                "Collector WebSocket {SessionId} failed while receiving messages.",
                request.SessionId.Value);
            return UnitResult.Failure(
                CollectorRuntimeErrors.ReceiveFailed(request.SessionId));
        }
        catch (Exception exception)
        {
            if (IsStopping())
                return UnitResult.Success<Error>();

            logger.LogError(
                exception,
                "Collector WebSocket {SessionId} failed unexpectedly.",
                request.SessionId.Value);
            return UnitResult.Failure(
                CollectorRuntimeErrors.ReceiveFailed(request.SessionId));
        }
    }

    private async Task<UnitResult<Error>> ReceiveLoopCoreAsync(
        ICollectorWebSocketConnection connection,
        ConnectionReadinessState state,
        CancellationToken receiveToken)
    {
        var frameBuffer = ArrayPool<byte>.Shared.Rent(options.ReceiveBufferSize);
        var messageBuffer = new ArrayBufferWriter<byte>(options.ReceiveBufferSize);

        try
        {
            while (true)
            {
                var frame = await connection.ReceiveAsync(
                    frameBuffer.AsMemory(0, options.ReceiveBufferSize),
                    receiveToken);

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

                var payload = messageBuffer.WrittenSpan.ToArray();
                messageBuffer.Clear();

                var isPong = IsPong(payload);
                if (isPong || IsPing(payload))
                {
                    if (isPong)
                    {
                        state.ObservePong(timeProvider.GetTimestamp());
                        await TryCompleteReadinessAsync(state, receiveToken);
                    }

                    continue;
                }

                var message = new RawMarketMessage(
                    request.SessionId,
                    state.Epoch,
                    timeProvider.GetUtcNow(),
                    payload);
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
                var observation = ReadinessObservation.TryRead(
                    payload,
                    request.Market.ConditionId,
                    state.ExpectedTokenIds);
                if (observation.IsProtocolViolation && state.IsReady)
                {
                    return UnitResult.Failure(
                        CollectorRuntimeErrors.ProtocolViolation(request.SessionId));
                }

                if (observation.TokenId is not null)
                {
                    var readinessResult = await readinessDispatcher.RecordInitialBookEnqueuedAsync(
                        request.SessionId,
                        TokenId.Create(observation.TokenId).Value,
                        state.Epoch,
                        timeProvider.GetUtcNow(),
                        receiveToken);
                    if (readinessResult.IsFailure)
                        return UnitResult.Failure(readinessResult.Error);

                    state.ObserveInitialBook(observation.TokenId);
                    await TryCompleteReadinessAsync(state, receiveToken);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(frameBuffer);
        }
    }

    private async Task<Result<ICollectorWebSocketConnection, Error>> ConnectAndSubscribeAsync(
        long epoch,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not "ws" and not "wss")
        {
            return Result.Failure<ICollectorWebSocketConnection, Error>(
                CollectorRuntimeErrors.InvalidEndpoint(request.SessionId));
        }

        ICollectorWebSocketConnection? connection = null;

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
                _startupConnection = null;
                _activeConnection = connection;
            }

            logger.LogInformation(
                "Collector WebSocket {SessionId} connected for market {MarketId} at epoch {ConnectionEpoch}.",
                request.SessionId.Value,
                request.Market.MarketId.Value,
                epoch);

            var connected = connection;
            connection = null;
            return Result.Success<ICollectorWebSocketConnection, Error>(connected);
        }
        catch (OperationCanceledException) when (_receiveCts.IsCancellationRequested
                                               || applicationLifetime.ApplicationStopping.IsCancellationRequested)
        {
            return Result.Failure<ICollectorWebSocketConnection, Error>(
                CollectorRuntimeErrors.StartCancelled(request.SessionId));
        }
        catch (OperationCanceledException)
        {
            return Result.Failure<ICollectorWebSocketConnection, Error>(
                CollectorRuntimeErrors.StartTimedOut(
                    request.SessionId,
                    options.ConnectTimeout));
        }
        catch (Exception exception) when
            (exception is WebSocketException or IOException or InvalidOperationException)
        {
            logger.LogError(
                exception,
                "Collector WebSocket {SessionId} failed during reconnect startup.",
                request.SessionId.Value);
            return Result.Failure<ICollectorWebSocketConnection, Error>(
                CollectorRuntimeErrors.StartFailed(request.SessionId));
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_startupConnection, connection))
                    _startupConnection = null;
            }

            connection?.Dispose();
        }
    }

    private async Task<UnitResult<Error>> HeartbeatLoopAsync(
        ICollectorWebSocketConnection connection,
        ConnectionReadinessState state,
        CancellationTokenSource heartbeatCts)
    {
        try
        {
            while (!heartbeatCts.IsCancellationRequested)
            {
                var now = timeProvider.GetTimestamp();
                var timedOut = state.IsHeartbeatTimedOut(
                    now,
                    options.HeartbeatTimeout,
                    timeProvider);
                if (timedOut)
                {
                    heartbeatCts.Cancel();
                    return UnitResult.Failure(
                        CollectorRuntimeErrors.HeartbeatTimedOut(
                            request.SessionId,
                            options.HeartbeatTimeout));
                }

                if (state.CanSendPing(now, options.HeartbeatInterval, timeProvider))
                {
                    await connection.SendTextAsync("PING"u8.ToArray(), heartbeatCts.Token);
                    state.ObservePingSent(now);
                }

                var delay = state.GetHeartbeatDelay(
                    now,
                    options.HeartbeatInterval,
                    options.HeartbeatTimeout,
                    timeProvider);
                await Task.Delay(delay, timeProvider, heartbeatCts.Token);
            }
        }
        catch (OperationCanceledException) when (heartbeatCts.IsCancellationRequested)
        {
        }
        catch (Exception exception) when
            (exception is WebSocketException or IOException or InvalidOperationException)
        {
            logger.LogError(
                exception,
                "Collector WebSocket {SessionId} failed while sending heartbeat.",
                request.SessionId.Value);
            return UnitResult.Failure(
                CollectorRuntimeErrors.ReceiveFailed(request.SessionId));
        }

        return UnitResult.Success<Error>();
    }

    private async Task TryCompleteReadinessAsync(
        ConnectionReadinessState state,
        CancellationToken cancellationToken)
    {
        if (state.IsReady || !state.HasAllInitialBooks)
            return;

        if (!state.AwaitingHeartbeatPersisted)
        {
            var result = await readinessDispatcher.MarkAwaitingHeartbeatAsync(
                request.SessionId,
                cancellationToken);
            if (result.IsFailure)
                return;

            state.AwaitingHeartbeatPersisted = true;
        }

        if (!state.HasMatchingPong)
            return;

        var readyAt = timeProvider.GetUtcNow();
        if (readyAt > request.ReadinessDeadline)
            return;

        var runningResult = await readinessDispatcher.MarkRunningAsync(
            request.SessionId,
            readyAt,
            cancellationToken);
        if (runningResult.IsSuccess)
            state.MarkReady();
    }

    private static bool IsPing(byte[] payload) =>
        payload.AsSpan().SequenceEqual("PING"u8);

    private static bool IsPong(byte[] payload) =>
        payload.AsSpan().SequenceEqual("PONG"u8);

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

    private sealed class ConnectionReadinessState(
        long epoch,
        string conditionId,
        IReadOnlyCollection<string> expectedTokenIds)
    {
        private readonly HashSet<string> _initialBookTokenIds = [];
        private long? _lastPingSentTimestamp;
        private long? _outstandingPingTimestamp;
        private bool _hasMatchingPong;

        public long Epoch { get; } = epoch;

        public string ConditionId { get; } = conditionId;

        public HashSet<string> ExpectedTokenIds { get; } = expectedTokenIds.ToHashSet();

        public bool HasAllInitialBooks => _initialBookTokenIds.Count == ExpectedTokenIds.Count;

        public bool HasMatchingPong => _hasMatchingPong;

        public bool IsReady { get; private set; }

        public bool AwaitingHeartbeatPersisted { get; set; }

        public ConnectionReadinessState NextEpoch() =>
            new(Epoch + 1, ConditionId, ExpectedTokenIds);

        public void ObserveInitialBook(string tokenId)
        {
            _initialBookTokenIds.Add(tokenId);
        }

        public void ObservePingSent(long timestamp)
        {
            _lastPingSentTimestamp = timestamp;
            _outstandingPingTimestamp = timestamp;
            _hasMatchingPong = false;
        }

        public void ObservePong(long timestamp)
        {
            if (_outstandingPingTimestamp is null)
                return;

            _outstandingPingTimestamp = null;
            _hasMatchingPong = true;
            _lastPingSentTimestamp = timestamp;
        }

        public bool CanSendPing(
            long now,
            TimeSpan interval,
            TimeProvider timeProvider)
        {
            if (_outstandingPingTimestamp is not null)
                return false;

            return _lastPingSentTimestamp is null
                   || timeProvider.GetElapsedTime(_lastPingSentTimestamp.Value, now) >= interval;
        }

        public bool IsHeartbeatTimedOut(
            long now,
            TimeSpan timeout,
            TimeProvider timeProvider)
        {
            return _outstandingPingTimestamp is not null
                   && timeProvider.GetElapsedTime(_outstandingPingTimestamp.Value, now) >= timeout;
        }

        public TimeSpan GetHeartbeatDelay(
            long now,
            TimeSpan interval,
            TimeSpan timeout,
            TimeProvider timeProvider)
        {
            var remaining = _outstandingPingTimestamp is null
                ? interval - (_lastPingSentTimestamp is null
                    ? interval
                    : timeProvider.GetElapsedTime(_lastPingSentTimestamp.Value, now))
                : timeout - timeProvider.GetElapsedTime(_outstandingPingTimestamp.Value, now);

            return remaining <= TimeSpan.Zero
                ? TimeSpan.FromMilliseconds(1)
                : remaining;
        }

        public void MarkReady()
        {
            IsReady = true;
        }
    }

    private sealed record ReadinessObservation(
        string? TokenId,
        bool IsProtocolViolation)
    {
        public static ReadinessObservation TryRead(
            byte[] payload,
            string conditionId,
            HashSet<string> expectedTokenIds)
        {
            try
            {
                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in root.EnumerateArray())
                    {
                        var observation = TryReadObject(
                            item,
                            conditionId,
                            expectedTokenIds);
                        if (observation.TokenId is not null || observation.IsProtocolViolation)
                            return observation;
                    }

                    return new ReadinessObservation(null, false);
                }

                return TryReadObject(root, conditionId, expectedTokenIds);
            }
            catch (JsonException)
            {
                return new ReadinessObservation(null, true);
            }
        }

        private static ReadinessObservation TryReadObject(
            JsonElement element,
            string conditionId,
            HashSet<string> expectedTokenIds)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return new ReadinessObservation(null, true);

            if (!element.TryGetProperty("event_type", out var eventType)
                || eventType.GetString() != "book")
            {
                return new ReadinessObservation(null, false);
            }

            if (!TryGetString(element, "market", out var market)
                || !string.Equals(market, conditionId, StringComparison.OrdinalIgnoreCase)
                || !TryGetString(element, "asset_id", out var tokenId)
                || !expectedTokenIds.Contains(tokenId)
                || !element.TryGetProperty("bids", out var bids)
                || bids.ValueKind != JsonValueKind.Array
                || !element.TryGetProperty("asks", out var asks)
                || asks.ValueKind != JsonValueKind.Array)
            {
                return new ReadinessObservation(null, true);
            }

            return new ReadinessObservation(tokenId, false);
        }

        private static bool TryGetString(
            JsonElement element,
            string propertyName,
            out string value)
        {
            value = string.Empty;
            if (!element.TryGetProperty(propertyName, out var property)
                || property.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value = property.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
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
