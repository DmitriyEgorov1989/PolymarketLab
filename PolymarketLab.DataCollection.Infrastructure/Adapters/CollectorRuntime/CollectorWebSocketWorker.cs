using CSharpFunctionalExtensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime.WebSockets;
using PolymarketLab.SharedKernel.Errors;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;

internal sealed class CollectorWebSocketWorker(
    CollectorRuntimeStartRequest request,
    ICollectorWebSocketFactory webSocketFactory,
    CollectorWebSocketOptions options,
    IHostApplicationLifetime applicationLifetime,
    ILogger<CollectorWebSocketWorker> logger)
    : ICollectorWorker
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _lifetimeCts =
        CancellationTokenSource.CreateLinkedTokenSource(
            applicationLifetime.ApplicationStopping);
    private ICollectorWebSocketConnection? _connection;
    private bool _stopRequested;
    private bool _lifetimeDisposed;

    public async Task<UnitResult<Error>> StartAsync(
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_stopRequested)
                return StartCancelled();
        }

        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not "ws" and not "wss")
        {
            DisposeLifetime();
            return UnitResult.Failure(
                CollectorRuntimeErrors.InvalidEndpoint(request.SessionId));
        }

        ICollectorWebSocketConnection? connection = null;

        try
        {
            using var startupCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCts.Token);
            startupCts.CancelAfter(options.ConnectTimeout);

            connection = webSocketFactory.Create();
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
                if (_stopRequested || _lifetimeCts.IsCancellationRequested)
                    return StartCancelled();

                _connection = connection;
                connection = null;
            }

            logger.LogInformation(
                "Collector WebSocket {SessionId} connected for market {MarketId}.",
                request.SessionId.Value,
                request.Market.MarketId.Value);

            return UnitResult.Success<Error>();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            CancelAndDisposeLifetime();
            throw;
        }
        catch (OperationCanceledException)
            when (_lifetimeCts.IsCancellationRequested)
        {
            return StartCancelled();
        }
        catch (OperationCanceledException)
        {
            CancelAndDisposeLifetime();
            return UnitResult.Failure(
                CollectorRuntimeErrors.StartTimedOut(
                    request.SessionId,
                    options.ConnectTimeout));
        }
        catch (Exception exception) when
            (exception is WebSocketException or IOException or InvalidOperationException)
        {
            CancelAndDisposeLifetime();
            logger.LogError(
                exception,
                "Collector WebSocket {SessionId} failed during startup.",
                request.SessionId.Value);

            return UnitResult.Failure(
                CollectorRuntimeErrors.StartFailed(request.SessionId));
        }
        finally
        {
            connection?.Dispose();
        }
    }

    public async Task<UnitResult<Error>> StopAsync(
        CancellationToken cancellationToken)
    {
        ICollectorWebSocketConnection? connection;

        lock (_sync)
        {
            _stopRequested = true;
            connection = _connection;
            _connection = null;
        }

        CancelLifetime();

        if (connection is null)
            return UnitResult.Success<Error>();

        try
        {
            await connection.CloseAsync(cancellationToken);
            return UnitResult.Success<Error>();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
        finally
        {
            connection.Dispose();
            DisposeLifetime();
        }
    }

    private UnitResult<Error> StartCancelled()
    {
        CancelAndDisposeLifetime();
        return UnitResult.Failure(
            CollectorRuntimeErrors.StartCancelled(request.SessionId));
    }

    private void CancelAndDisposeLifetime()
    {
        CancelLifetime();
        DisposeLifetime();
    }

    private void CancelLifetime()
    {
        lock (_sync)
        {
            if (!_lifetimeDisposed)
                _lifetimeCts.Cancel();
        }
    }

    private void DisposeLifetime()
    {
        lock (_sync)
        {
            if (_lifetimeDisposed)
                return;

            _lifetimeCts.Dispose();
            _lifetimeDisposed = true;
        }
    }

    private sealed record MarketSubscription(
        [property: JsonPropertyName("assets_ids")] string[] AssetIds,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("custom_feature_enabled")] bool CustomFeatureEnabled);
}
