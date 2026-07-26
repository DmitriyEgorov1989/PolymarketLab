using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime.WebSockets;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using System.Net.WebSockets;
using System.Text.Json;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.CollectorRuntime;

public sealed class CollectorWebSocketWorkerTests
{
    [Fact]
    public async Task StartAsync_ShouldConnectAndSendMarketSubscription()
    {
        var connection = new StubWebSocketConnection();
        var request = CreateRequest();
        var worker = CreateWorker(request, connection);

        var result = await worker.StartAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        connection.Endpoint.Should().Be(
            new Uri("wss://ws-subscriptions-clob.polymarket.com/ws/market"));
        connection.ConnectCallCount.Should().Be(1);
        connection.SendCallCount.Should().Be(1);
        connection.IsDisposed.Should().BeFalse();

        using var subscription = JsonDocument.Parse(connection.SentMessage!);
        var root = subscription.RootElement;
        root.GetProperty("type").GetString().Should().Be("market");
        root.GetProperty("custom_feature_enabled").GetBoolean().Should().BeTrue();
        root.GetProperty("assets_ids")
            .EnumerateArray()
            .Select(element => element.GetString())
            .Should()
            .Equal("yes-token", "no-token");
    }

    [Fact]
    public async Task StartAsync_WithCustomFeaturesDisabled_ShouldSendOptionValue()
    {
        var connection = new StubWebSocketConnection();
        var options = CreateOptions(customFeatureEnabled: false);
        var worker = CreateWorker(CreateRequest(), connection, options);

        var result = await worker.StartAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        using var subscription = JsonDocument.Parse(connection.SentMessage!);
        subscription.RootElement
            .GetProperty("custom_feature_enabled")
            .GetBoolean()
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task StartAsync_WhenCallerCancels_ShouldDisposeConnectionAndRethrow()
    {
        var connectEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = new StubWebSocketConnection
        {
            ConnectHandler = async cancellationToken =>
            {
                connectEntered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        };
        var worker = CreateWorker(CreateRequest(), connection);
        using var cancellationTokenSource = new CancellationTokenSource();

        var startTask = worker.StartAsync(cancellationTokenSource.Token);
        await connectEntered.Task;
        cancellationTokenSource.Cancel();

        Func<Task> start = async () => await startTask;
        await start.Should().ThrowAsync<OperationCanceledException>();
        connection.IsDisposed.Should().BeTrue();
        connection.SendCallCount.Should().Be(0);
    }

    [Fact]
    public async Task StartAsync_WhenStartupTimesOut_ShouldReturnTimeoutAndDisposeConnection()
    {
        var connection = new StubWebSocketConnection
        {
            ConnectHandler = cancellationToken =>
                Task.FromException(new OperationCanceledException(cancellationToken))
        };
        var request = CreateRequest();
        var worker = CreateWorker(request, connection);

        var result = await worker.StartAsync(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.runtime.start.timeout");
        connection.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_WhenApplicationIsStopping_ShouldReturnCancelledAndDisposeConnection()
    {
        var connection = new StubWebSocketConnection
        {
            ConnectHandler = cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
        };
        var lifetime = new StubHostApplicationLifetime();
        lifetime.StopApplication();
        var request = CreateRequest();
        var worker = CreateWorker(request, connection, lifetime: lifetime);

        var result = await worker.StartAsync(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.runtime.start.cancelled");
        connection.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_WhenTransportFails_ShouldReturnFailureAndDisposeConnection()
    {
        var connection = new StubWebSocketConnection
        {
            ConnectHandler = _ => Task.FromException(
                new WebSocketException("Connection failure."))
        };
        var request = CreateRequest();
        var worker = CreateWorker(request, connection);

        var result = await worker.StartAsync(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.runtime.start.failed");
        connection.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_WithInvalidEndpoint_ShouldNotCreateConnection()
    {
        var connection = new StubWebSocketConnection();
        var factory = new StubWebSocketFactory(connection);
        var request = CreateRequest();
        var options = new CollectorWebSocketOptions
        {
            Endpoint = "https://example.com/ws",
            ConnectTimeout = TimeSpan.FromSeconds(10),
            CustomFeatureEnabled = true
        };
        var worker = new CollectorWebSocketWorker(
            request,
            factory,
            options,
            new StubHostApplicationLifetime(),
            NullLogger<CollectorWebSocketWorker>.Instance);

        var result = await worker.StartAsync(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.runtime.endpoint.invalid");
        factory.CreateCallCount.Should().Be(0);
    }

    [Fact]
    public async Task StopAsync_AfterSuccessfulStart_ShouldCloseAndDisposeConnection()
    {
        var connection = new StubWebSocketConnection();
        var worker = CreateWorker(CreateRequest(), connection);
        await worker.StartAsync(CancellationToken.None);

        var result = await worker.StopAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        connection.CloseCallCount.Should().Be(1);
        connection.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_DuringStartup_ShouldCancelStartupWithoutDisposingCtsEarly()
    {
        var connectEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = new StubWebSocketConnection
        {
            ConnectHandler = async cancellationToken =>
            {
                connectEntered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        };
        var worker = CreateWorker(CreateRequest(), connection);

        var startTask = worker.StartAsync(CancellationToken.None);
        await connectEntered.Task;
        var stopResult = await worker.StopAsync(CancellationToken.None);
        var startResult = await startTask;

        stopResult.IsSuccess.Should().BeTrue();
        startResult.IsFailure.Should().BeTrue();
        startResult.Error.Code.Should().Be("collector.runtime.start.cancelled");
        connection.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_AfterStopBeforeStartup_ShouldReturnCancelled()
    {
        var connection = new StubWebSocketConnection();
        var worker = CreateWorker(CreateRequest(), connection);
        await worker.StopAsync(CancellationToken.None);

        var result = await worker.StartAsync(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.runtime.start.cancelled");
        connection.ConnectCallCount.Should().Be(0);
    }

    private static CollectorWebSocketWorker CreateWorker(
        CollectorRuntimeStartRequest request,
        StubWebSocketConnection connection,
        CollectorWebSocketOptions? options = null,
        IHostApplicationLifetime? lifetime = null)
    {
        return new CollectorWebSocketWorker(
            request,
            new StubWebSocketFactory(connection),
            options ?? CreateOptions(),
            lifetime ?? new StubHostApplicationLifetime(),
            NullLogger<CollectorWebSocketWorker>.Instance);
    }

    private static CollectorWebSocketOptions CreateOptions(
        bool customFeatureEnabled = true)
    {
        return new CollectorWebSocketOptions
        {
            CustomFeatureEnabled = customFeatureEnabled
        };
    }

    private static CollectorRuntimeStartRequest CreateRequest()
    {
        return new CollectorRuntimeStartRequest(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            new CollectionMarket(
                MarketId.Create(Guid.NewGuid()).Value,
                "runtime-test-market",
                [
                    new CollectionMarketToken(
                        TokenId.Create("yes-token").Value,
                        "Yes",
                        0),
                    new CollectionMarketToken(
                        TokenId.Create("no-token").Value,
                        "No",
                        1)
                ]));
    }

    private sealed class StubWebSocketFactory(StubWebSocketConnection connection)
        : ICollectorWebSocketFactory
    {
        public int CreateCallCount { get; private set; }

        public ICollectorWebSocketConnection Create()
        {
            CreateCallCount++;
            return connection;
        }
    }

    private sealed class StubWebSocketConnection : ICollectorWebSocketConnection
    {
        public Func<CancellationToken, Task>? ConnectHandler { get; init; }
        public Uri? Endpoint { get; private set; }
        public byte[]? SentMessage { get; private set; }
        public int ConnectCallCount { get; private set; }
        public int SendCallCount { get; private set; }
        public int CloseCallCount { get; private set; }
        public bool IsDisposed { get; private set; }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            ConnectCallCount++;
            Endpoint = endpoint;
            return ConnectHandler?.Invoke(cancellationToken) ?? Task.CompletedTask;
        }

        public Task SendTextAsync(
            ReadOnlyMemory<byte> message,
            CancellationToken cancellationToken)
        {
            SendCallCount++;
            SentMessage = message.ToArray();
            return Task.CompletedTask;
        }

        public Task CloseAsync(CancellationToken cancellationToken)
        {
            CloseCallCount++;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class StubHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication()
        {
            _stopping.Cancel();
        }
    }
}
