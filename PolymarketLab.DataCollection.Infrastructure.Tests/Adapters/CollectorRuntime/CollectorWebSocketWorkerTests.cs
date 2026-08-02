using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime.WebSockets;
using PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;
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
    public async Task ReceiveAsync_WithTextMessage_ShouldEnqueueRawPayload()
    {
        var connection = new StubWebSocketConnection();
        connection.AddFrame("{\"price\":0.5}"u8);
        var sink = new StubRawMarketMessageSink();
        var request = CreateRequest();
        var telemetry = new RawMarketMessageTelemetry();
        var receivedAt = DateTimeOffset.Parse("2026-07-27T12:00:00Z");
        var worker = CreateWorker(
            request,
            connection,
            messageSink: sink,
            telemetry: telemetry,
            timeProvider: new StubTimeProvider(receivedAt));

        var startResult = await worker.StartAsync(CancellationToken.None);
        var message = await sink.WaitForMessageAsync();
        await worker.StopAsync(CancellationToken.None);

        startResult.IsSuccess.Should().BeTrue();
        message.SessionId.Should().Be(request.SessionId);
        message.ReceivedAt.Should().Be(receivedAt);
        message.Payload.Should().Equal("{\"price\":0.5}"u8.ToArray());
        telemetry.GetSnapshot(request.SessionId).Should().Be(
            new RawMarketMessageCounters(1, 1, 0));
    }

    [Fact]
    public async Task ReceiveAsync_WithFragmentedText_ShouldEnqueueOneMessage()
    {
        var connection = new StubWebSocketConnection();
        connection.AddFrame("hel"u8, endOfMessage: false);
        connection.AddFrame("lo"u8);
        var sink = new StubRawMarketMessageSink();
        var worker = CreateWorker(
            CreateRequest(),
            connection,
            messageSink: sink);

        await worker.StartAsync(CancellationToken.None);
        var message = await sink.WaitForMessageAsync();
        await worker.StopAsync(CancellationToken.None);

        message.Payload.Should().Equal("hello"u8.ToArray());
        connection.ReceiveCallCount.Should().Be(3);
    }

    [Fact]
    public async Task ReceiveAsync_WithBinaryMessage_ShouldCompleteWithFailure()
    {
        var connection = new StubWebSocketConnection();
        connection.AddFrame([1, 2, 3], WebSocketMessageType.Binary);
        var worker = CreateWorker(CreateRequest(), connection);

        var startResult = await worker.StartAsync(CancellationToken.None);
        var completion = await worker.Completion;

        startResult.IsSuccess.Should().BeTrue();
        completion.Result.IsFailure.Should().BeTrue();
        completion.Result.Error.Code.Should().Be(
            "collector.runtime.receive.message_type.unsupported");
        completion.Origin.Should().Be(CollectorWorkerCompletionOrigin.Autonomous);
        connection.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task ReceiveAsync_WhenFragmentedMessageIsTooLarge_ShouldCompleteWithFailure()
    {
        var connection = new StubWebSocketConnection();
        connection.AddFrame("1234"u8, endOfMessage: false);
        connection.AddFrame("56"u8);
        var options = new CollectorWebSocketOptions
        {
            ReceiveBufferSize = 4,
            MaximumMessageSize = 5
        };
        var worker = CreateWorker(CreateRequest(), connection, options);

        await worker.StartAsync(CancellationToken.None);
        var completion = await worker.Completion;

        completion.Result.IsFailure.Should().BeTrue();
        completion.Result.Error.Code.Should().Be(
            "collector.runtime.receive.message_too_large");
        completion.Origin.Should().Be(CollectorWorkerCompletionOrigin.Autonomous);
    }

    [Fact]
    public async Task ReceiveAsync_WhenRemoteCloses_ShouldCompleteWithFailure()
    {
        var connection = new StubWebSocketConnection();
        connection.AddFrame([], WebSocketMessageType.Close);
        var worker = CreateWorker(CreateRequest(), connection);

        await worker.StartAsync(CancellationToken.None);
        var completion = await worker.Completion;

        completion.Result.IsFailure.Should().BeTrue();
        completion.Result.Error.Code.Should().Be("collector.runtime.receive.closed");
        completion.Origin.Should().Be(CollectorWorkerCompletionOrigin.Autonomous);
        connection.CloseCallCount.Should().Be(1);
        connection.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task ReceiveAsync_WhenTransportFails_ShouldCompleteWithFailure()
    {
        var connection = new StubWebSocketConnection
        {
            ReceiveHandler = (_, _) => ValueTask.FromException<
                CollectorWebSocketReceiveResult>(
                new WebSocketException("Receive failure."))
        };
        var worker = CreateWorker(CreateRequest(), connection);

        await worker.StartAsync(CancellationToken.None);
        var completion = await worker.Completion;

        completion.Result.IsFailure.Should().BeTrue();
        completion.Result.Error.Code.Should().Be("collector.runtime.receive.failed");
        completion.Origin.Should().Be(CollectorWorkerCompletionOrigin.Autonomous);
    }

    [Fact]
    public async Task ReceiveAsync_WhenSinkIsBlocked_ShouldApplyBackpressure()
    {
        var enqueueEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseEnqueue = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = new StubWebSocketConnection();
        connection.AddFrame("first"u8);
        connection.AddFrame("second"u8);
        var sink = new StubRawMarketMessageSink
        {
            Handler = async (_, cancellationToken) =>
            {
                enqueueEntered.TrySetResult();
                await releaseEnqueue.Task.WaitAsync(cancellationToken);
            }
        };
        var worker = CreateWorker(
            CreateRequest(),
            connection,
            messageSink: sink);

        await worker.StartAsync(CancellationToken.None);
        await enqueueEntered.Task;
        connection.ReceiveCallCount.Should().Be(1);

        releaseEnqueue.SetResult();
        await sink.WaitForMessageAsync();
        await sink.WaitForMessageAsync();
        connection.ReceiveCallCount.Should().Be(3);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_WhenEnqueueIsCancelledAfterCompleteMessage_ShouldFail()
    {
        var enqueueEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = new StubWebSocketConnection();
        connection.AddFrame("complete"u8);
        var sink = new StubRawMarketMessageSink
        {
            Handler = async (_, cancellationToken) =>
            {
                enqueueEntered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        };
        var request = CreateRequest();
        var telemetry = new RawMarketMessageTelemetry();
        var worker = CreateWorker(
            request,
            connection,
            options: new CollectorWebSocketOptions
            {
                StopTimeout = TimeSpan.FromMilliseconds(20)
            },
            messageSink: sink,
            telemetry: telemetry);

        await worker.StartAsync(CancellationToken.None);
        await enqueueEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stopResult = await worker.StopAsync(CancellationToken.None);
        var completion = await worker.Completion;

        stopResult.IsFailure.Should().BeTrue();
        completion.Result.IsFailure.Should().BeTrue();
        completion.Result.Error.Code.Should().Be(
            "collector.runtime.ingestion.enqueue_cancelled");
        completion.Origin.Should().Be(CollectorWorkerCompletionOrigin.Autonomous);
        telemetry.GetSnapshot(request.SessionId).Should().Be(
            new RawMarketMessageCounters(1, 0, 0));
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
            new StubRawMarketMessageSink(),
            new RawMarketMessageTelemetry(),
            TimeProvider.System,
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
        var completion = await worker.Completion;

        result.IsSuccess.Should().BeTrue();
        completion.Origin.Should().Be(
            CollectorWorkerCompletionOrigin.RequestedStop);
        connection.CloseCallCount.Should().Be(1);
        connection.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task Completion_WhenApplicationStops_ShouldIdentifyShutdownOrigin()
    {
        var connection = new StubWebSocketConnection();
        var lifetime = new StubHostApplicationLifetime();
        var worker = CreateWorker(
            CreateRequest(),
            connection,
            lifetime: lifetime);
        await worker.StartAsync(CancellationToken.None);

        lifetime.StopApplication();
        await worker.StopAsync(CancellationToken.None);
        var completion = await worker.Completion;

        completion.Result.IsSuccess.Should().BeTrue();
        completion.Origin.Should().Be(
            CollectorWorkerCompletionOrigin.ApplicationShutdown);
    }

    [Fact]
    public async Task StopAsync_WhenCancelledReceiveThrowsTransportError_ShouldRemainRequested()
    {
        var connection = new StubWebSocketConnection
        {
            ReceiveHandler = async (_, cancellationToken) =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw new WebSocketException("Receive aborted by stop.");
                }

                throw new InvalidOperationException("Unreachable receive continuation.");
            }
        };
        var worker = CreateWorker(CreateRequest(), connection);
        await worker.StartAsync(CancellationToken.None);

        var stopResult = await worker.StopAsync(CancellationToken.None);
        var completion = await worker.Completion;

        stopResult.IsSuccess.Should().BeTrue();
        completion.Result.IsSuccess.Should().BeTrue();
        completion.Origin.Should().Be(
            CollectorWorkerCompletionOrigin.RequestedStop);
    }

    [Fact]
    public async Task StopAsync_WhenShutdownTimesOut_ShouldReturnTimeoutAndDisposeConnection()
    {
        var connection = new StubWebSocketConnection
        {
            CloseHandler = cancellationToken => Task.FromException(
                new OperationCanceledException(cancellationToken))
        };
        var worker = CreateWorker(CreateRequest(), connection);
        await worker.StartAsync(CancellationToken.None);

        var result = await worker.StopAsync(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.runtime.stop.timeout");
        connection.CloseCancellationToken.CanBeCanceled.Should().BeTrue();
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
    public async Task StopAsync_WhenStartupIgnoresCancellation_ShouldWaitForCleanup()
    {
        var connectEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseConnect = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = new StubWebSocketConnection
        {
            ConnectHandler = async cancellationToken =>
            {
                connectEntered.SetResult();
                await releaseConnect.Task;
                cancellationToken.ThrowIfCancellationRequested();
            }
        };
        var worker = CreateWorker(CreateRequest(), connection);

        var startTask = worker.StartAsync(CancellationToken.None);
        await connectEntered.Task;
        var stopTask = worker.StopAsync(CancellationToken.None);

        stopTask.IsCompleted.Should().BeFalse();
        connection.IsDisposed.Should().BeFalse();

        releaseConnect.SetResult();
        var stopResult = await stopTask;
        var startResult = await startTask;

        stopResult.IsSuccess.Should().BeTrue();
        startResult.IsFailure.Should().BeTrue();
        connection.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_WhenStartupExceedsDeadline_ShouldAbortConnection()
    {
        var connectEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseConnect = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = new StubWebSocketConnection
        {
            ConnectHandler = async cancellationToken =>
            {
                connectEntered.SetResult();
                await releaseConnect.Task;
                cancellationToken.ThrowIfCancellationRequested();
            }
        };
        var options = new CollectorWebSocketOptions
        {
            StopTimeout = TimeSpan.FromMilliseconds(20)
        };
        var worker = CreateWorker(CreateRequest(), connection, options);

        var startTask = worker.StartAsync(CancellationToken.None);
        await connectEntered.Task;
        var stopResult = await worker
            .StopAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));

        stopResult.IsFailure.Should().BeTrue();
        stopResult.Error.Code.Should().Be("collector.runtime.stop.timeout");
        connection.IsDisposed.Should().BeTrue();

        releaseConnect.SetResult();
        (await startTask).IsFailure.Should().BeTrue();
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
        IHostApplicationLifetime? lifetime = null,
        StubRawMarketMessageSink? messageSink = null,
        RawMarketMessageTelemetry? telemetry = null,
        TimeProvider? timeProvider = null)
    {
        return new CollectorWebSocketWorker(
            request,
            new StubWebSocketFactory(connection),
            options ?? CreateOptions(),
            messageSink ?? new StubRawMarketMessageSink(),
            telemetry ?? new RawMarketMessageTelemetry(),
            timeProvider ?? TimeProvider.System,
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
        private readonly Queue<StubFrame> _frames = new();

        public Func<CancellationToken, Task>? ConnectHandler { get; init; }
        public Func<CancellationToken, Task>? CloseHandler { get; init; }
        public Func<Memory<byte>, CancellationToken,
            ValueTask<CollectorWebSocketReceiveResult>>? ReceiveHandler { get; init; }
        public Uri? Endpoint { get; private set; }
        public byte[]? SentMessage { get; private set; }
        public int ConnectCallCount { get; private set; }
        public int SendCallCount { get; private set; }
        public int CloseCallCount { get; private set; }
        public int ReceiveCallCount { get; private set; }
        public bool IsDisposed { get; private set; }
        public CancellationToken CloseCancellationToken { get; private set; }

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

        public ValueTask<CollectorWebSocketReceiveResult> ReceiveAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken)
        {
            ReceiveCallCount++;

            if (ReceiveHandler is not null)
                return ReceiveHandler(buffer, cancellationToken);

            if (_frames.TryDequeue(out var frame))
            {
                frame.Payload.CopyTo(buffer);
                return ValueTask.FromResult(new CollectorWebSocketReceiveResult(
                    frame.Payload.Length,
                    frame.MessageType,
                    frame.EndOfMessage));
            }

            return WaitForCancellationAsync(cancellationToken);
        }

        public Task CloseAsync(CancellationToken cancellationToken)
        {
            CloseCallCount++;
            CloseCancellationToken = cancellationToken;
            return CloseHandler?.Invoke(cancellationToken) ?? Task.CompletedTask;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }

        public void AddFrame(
            ReadOnlySpan<byte> payload,
            WebSocketMessageType messageType = WebSocketMessageType.Text,
            bool endOfMessage = true)
        {
            _frames.Enqueue(new StubFrame(
                payload.ToArray(),
                messageType,
                endOfMessage));
        }

        private static async ValueTask<CollectorWebSocketReceiveResult>
            WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable receive continuation.");
        }

        private sealed record StubFrame(
            byte[] Payload,
            WebSocketMessageType MessageType,
            bool EndOfMessage);
    }

    private sealed class StubRawMarketMessageSink : IRawMarketMessageSink
    {
        private readonly List<RawMarketMessage> _messages = [];
        private readonly SemaphoreSlim _messageAvailable = new(0);

        public Func<RawMarketMessage, CancellationToken, ValueTask>? Handler
        {
            get;
            init;
        }

        public async ValueTask EnqueueAsync(
            RawMarketMessage message,
            CancellationToken cancellationToken)
        {
            if (Handler is not null)
                await Handler(message, cancellationToken);

            lock (_messages)
            {
                _messages.Add(message);
            }

            _messageAvailable.Release();
        }

        public async Task<RawMarketMessage> WaitForMessageAsync()
        {
            if (!await _messageAvailable.WaitAsync(TimeSpan.FromSeconds(2)))
                throw new TimeoutException("A raw market message was not observed.");

            lock (_messages)
            {
                return _messages[^1];
            }
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

    private sealed class StubTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
