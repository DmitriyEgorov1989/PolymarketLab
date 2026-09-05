using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime.WebSockets;
using PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using System.Net.WebSockets;
using System.Text;
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
        connection.SendCallCount.Should().BeGreaterThanOrEqualTo(1);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitialPing_WhenSendFails_ShouldCleanUpAndInvalidate(bool synchronousFailure)
    {
        var closeEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseClose = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = new StubWebSocketConnection
        {
            SendHandler = (message, _) =>
            {
                if (!message.Span.SequenceEqual("PING"u8))
                    return Task.CompletedTask;

                if (synchronousFailure)
                    throw new InvalidOperationException("Initial ping failure.");

                return Task.FromException(new WebSocketException("Initial ping failure."));
            },
            CloseHandler = async cancellationToken =>
            {
                closeEntered.SetResult();
                await releaseClose.Task.WaitAsync(cancellationToken);
            }
        };
        var dispatcher = new StubReadinessDispatcher();
        var request = CreateRequest();
        var worker = CreateWorker(
            request,
            connection,
            readinessDispatcher: dispatcher,
            timeProvider: new StubTimeProvider(request.ReadinessDeadline));

        var startResult = await worker.StartAsync(CancellationToken.None);
        await closeEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            worker.Completion.IsCompleted.Should().BeFalse();
            connection.IsDisposed.Should().BeFalse();
            dispatcher.InvalidationCount.Should().Be(0);
        }
        finally
        {
            releaseClose.TrySetResult();
        }

        var completion = await worker.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        startResult.IsSuccess.Should().BeTrue();
        completion.Result.IsFailure.Should().BeTrue();
        completion.Result.Error.Code.Should().Be("collector.runtime.receive.failed");
        completion.Origin.Should().Be(CollectorWorkerCompletionOrigin.Invalidated);
        dispatcher.InvalidationCount.Should().Be(1);
        dispatcher.RunningCount.Should().Be(0);
        connection.SendCallCount.Should().Be(2);
        connection.ReceiveCallCount.Should().Be(0);
        connection.CloseCallCount.Should().Be(1);
        connection.IsDisposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitialPing_WhenStopCancelsSend_ShouldCompleteSuccessfully(bool applicationShutdown)
    {
        var pingEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = new StubWebSocketConnection
        {
            SendHandler = async (message, cancellationToken) =>
            {
                if (!message.Span.SequenceEqual("PING"u8))
                    return;

                pingEntered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        };
        var dispatcher = new StubReadinessDispatcher();
        var lifetime = new StubHostApplicationLifetime();
        var request = CreateRequest();
        var worker = CreateWorker(
            request,
            connection,
            lifetime: lifetime,
            readinessDispatcher: dispatcher,
            timeProvider: new StubTimeProvider(request.ReadinessDeadline));

        var startResult = await worker.StartAsync(CancellationToken.None);
        await pingEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (applicationShutdown)
            lifetime.StopApplication();

        var stopResult = await worker.StopAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));
        var completion = await worker.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        startResult.IsSuccess.Should().BeTrue();
        stopResult.IsSuccess.Should().BeTrue();
        completion.Result.IsSuccess.Should().BeTrue();
        completion.Origin.Should().Be(applicationShutdown
            ? CollectorWorkerCompletionOrigin.ApplicationShutdown
            : CollectorWorkerCompletionOrigin.RequestedStop);
        dispatcher.InvalidationCount.Should().Be(0);
        dispatcher.RunningCount.Should().Be(0);
        connection.SendCallCount.Should().Be(2);
        connection.ReceiveCallCount.Should().Be(0);
        connection.CloseCallCount.Should().Be(1);
        connection.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task InitialReadiness_WhenStopCancelsPersistence_ShouldCleanUpAndCompleteSuccessfully()
    {
        var readinessEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new StubReadinessDispatcher
        {
            AwaitingInitialBooksHandler = async cancellationToken =>
            {
                readinessEntered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return UnitResult.Success<Error>();
            }
        };
        var connection = new StubWebSocketConnection();
        var worker = CreateWorker(
            CreateRequest(),
            connection,
            readinessDispatcher: dispatcher);

        var startResult = await worker.StartAsync(CancellationToken.None);
        await readinessEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stopResult = await worker.StopAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));
        var completion = await worker.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        startResult.IsSuccess.Should().BeTrue();
        stopResult.IsSuccess.Should().BeTrue();
        completion.Result.IsSuccess.Should().BeTrue();
        completion.Origin.Should().Be(CollectorWorkerCompletionOrigin.RequestedStop);
        dispatcher.AwaitingInitialBooksCount.Should().Be(1);
        dispatcher.InvalidationCount.Should().Be(0);
        dispatcher.RunningCount.Should().Be(0);
        connection.SendCallCount.Should().Be(1);
        connection.ReceiveCallCount.Should().Be(0);
        connection.CloseCallCount.Should().Be(1);
        connection.IsDisposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitialReadiness_WhenPersistenceFails_ShouldCleanUpAndPreserveError(bool stopApplication)
    {
        var lifetime = new StubHostApplicationLifetime();
        var persistenceError = new Error(
            "collector.runtime.readiness.persistence_failed",
            "Readiness persistence failed.",
            ErrorType.Failure);
        var dispatcher = new StubReadinessDispatcher
        {
            AwaitingInitialBooksHandler = _ =>
            {
                if (stopApplication)
                    lifetime.StopApplication();

                return Task.FromResult(UnitResult.Failure(persistenceError));
            }
        };
        var connection = new StubWebSocketConnection();
        var request = CreateRequest();
        var worker = CreateWorker(
            request,
            connection,
            lifetime: lifetime,
            readinessDispatcher: dispatcher,
            timeProvider: new StubTimeProvider(stopApplication
                ? request.ReadinessDeadline - TimeSpan.FromSeconds(10)
                : request.ReadinessDeadline));

        var startResult = await worker.StartAsync(CancellationToken.None);
        var completion = await worker.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        startResult.IsSuccess.Should().BeTrue();
        completion.Result.IsFailure.Should().BeTrue();
        completion.Result.Error.Should().BeSameAs(persistenceError);
        completion.Origin.Should().Be(stopApplication
            ? CollectorWorkerCompletionOrigin.Autonomous
            : CollectorWorkerCompletionOrigin.Invalidated);
        dispatcher.AwaitingInitialBooksCount.Should().Be(1);
        dispatcher.InvalidationCount.Should().Be(stopApplication ? 0 : 1);
        dispatcher.RunningCount.Should().Be(0);
        connection.SendCallCount.Should().Be(1);
        connection.ReceiveCallCount.Should().Be(0);
        connection.CloseCallCount.Should().Be(1);
        connection.IsDisposed.Should().BeTrue();
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
        message.ConnectionEpoch.Should().Be(1);
        message.ReceivedAt.Should().Be(receivedAt);
        message.Payload.Should().Equal("{\"price\":0.5}"u8.ToArray());
        telemetry.GetSnapshot(request.SessionId).Should().Be(
            new RawMarketMessageCounters(1, 1, 0, receivedAt, 0, 1));
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
    public async Task ReceiveAsync_AfterReconnect_ShouldUseNextConnectionEpoch()
    {
        var firstConnection = new StubWebSocketConnection();
        firstConnection.AddFrame("first"u8);
        firstConnection.AddFrame([], WebSocketMessageType.Close);
        var secondConnection = new StubWebSocketConnection();
        secondConnection.AddFrame("second"u8);
        var sink = new StubRawMarketMessageSink();
        var telemetry = new RawMarketMessageTelemetry();
        var request = CreateRequest();
        var worker = CreateWorker(
            request,
            firstConnection,
            options: new CollectorWebSocketOptions
            {
                ReconnectDelay = TimeSpan.FromMilliseconds(1)
            },
            messageSink: sink,
            telemetry: telemetry,
            timeProvider: new StubTimeProvider(
                DateTimeOffset.Parse("2026-08-28T11:59:40Z")),
            webSocketFactory: new StubWebSocketFactory(
                firstConnection,
                secondConnection));

        await worker.StartAsync(CancellationToken.None);
        var first = await sink.WaitForMessageAsync();
        var second = await sink.WaitForMessageAsync();
        await worker.StopAsync(CancellationToken.None);

        first.ConnectionEpoch.Should().Be(1);
        second.ConnectionEpoch.Should().Be(2);
        var checkpoint = telemetry.GetCheckpoint(request.SessionId);
        checkpoint.CurrentConnectionEpoch.Should().Be(2);
        checkpoint.ReconnectCount.Should().Be(1);
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
        completion.Origin.Should().Be(CollectorWorkerCompletionOrigin.Invalidated);
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
        completion.Origin.Should().Be(CollectorWorkerCompletionOrigin.Invalidated);
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
        completion.Origin.Should().Be(CollectorWorkerCompletionOrigin.Invalidated);
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
        completion.Origin.Should().Be(CollectorWorkerCompletionOrigin.Invalidated);
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
        await WaitUntilAsync(() => connection.ReceiveCallCount == 3);
        connection.ReceiveCallCount.Should().Be(3);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ReceiveAsync_WithInitialBooksAndHeartbeatMessages_ShouldNotEnqueueHeartbeatMessages()
    {
        var connection = new StubWebSocketConnection();
        connection.AddFrame(BookMessage("yes-token"));
        connection.AddFrame(BookMessage("no-token"));
        connection.AddFrame("PING"u8);
        connection.AddFrame("PONG"u8);
        var sink = new StubRawMarketMessageSink();
        var telemetry = new RawMarketMessageTelemetry();
        var dispatcher = new StubReadinessDispatcher();
        var request = CreateRequest();
        var worker = CreateWorker(
            request,
            connection,
            messageSink: sink,
            telemetry: telemetry,
            timeProvider: new StubTimeProvider(DateTimeOffset.Parse("2026-08-28T11:59:40Z")),
            readinessDispatcher: dispatcher);

        await worker.StartAsync(CancellationToken.None);
        await sink.WaitForMessageAsync();
        await sink.WaitForMessageAsync();
        await dispatcher.WaitForRunningAsync();
        await worker.StopAsync(CancellationToken.None);

        dispatcher.AwaitingInitialBooksCount.Should().Be(1);
        dispatcher.AwaitingHeartbeatCount.Should().Be(1);
        dispatcher.RunningCount.Should().Be(1);
        sink.Count.Should().Be(2);
        telemetry.GetSnapshot(request.SessionId).ReceivedComplete.Should().Be(2);
        telemetry.GetSnapshot(request.SessionId).Enqueued.Should().Be(2);
        connection.SentMessages
            .Select(Encoding.UTF8.GetString)
            .Should()
            .Contain("PING");
    }

    [Fact]
    public async Task ReceiveAsync_WithInitialBook_ShouldRecordTokenReadinessForCurrentEpoch()
    {
        var connection = new StubWebSocketConnection();
        connection.AddFrame(BookMessage("yes-token"));
        var sink = new StubRawMarketMessageSink();
        var dispatcher = new StubReadinessDispatcher();
        var request = CreateRequest();
        var enqueuedAt = DateTimeOffset.Parse("2026-08-28T11:59:44Z");
        var worker = CreateWorker(
            request,
            connection,
            messageSink: sink,
            readinessDispatcher: dispatcher,
            timeProvider: new StubTimeProvider(enqueuedAt));

        await worker.StartAsync(CancellationToken.None);
        await sink.WaitForMessageAsync();
        await WaitUntilAsync(() => dispatcher.TokenReadinessRecords.Count == 1);
        await worker.StopAsync(CancellationToken.None);

        dispatcher.TokenReadinessRecords.Should().ContainSingle().Which.Should()
            .Be((request.SessionId, "yes-token", 1L, enqueuedAt));
    }

    [Fact]
    public async Task ReceiveAsync_WhenReadinessRecordFails_ShouldInvalidateWithSameError()
    {
        var connection = new StubWebSocketConnection();
        connection.AddFrame(BookMessage("yes-token"));
        var sink = new StubRawMarketMessageSink();
        var persistenceError = new Error(
            "collector.runtime.readiness.persistence_failed",
            "Readiness persistence failed.",
            ErrorType.Failure);
        var dispatcher = new StubReadinessDispatcher
        {
            RecordInitialBookResult = UnitResult.Failure(persistenceError)
        };
        var worker = CreateWorker(
            CreateRequest(),
            connection,
            messageSink: sink,
            readinessDispatcher: dispatcher,
            timeProvider: new StubTimeProvider(
                DateTimeOffset.Parse("2026-08-28T11:59:50Z")));

        await worker.StartAsync(CancellationToken.None);
        var completion = await worker.Completion;

        completion.Result.IsFailure.Should().BeTrue();
        completion.Result.Error.Should().BeSameAs(persistenceError);
        completion.Origin.Should().Be(CollectorWorkerCompletionOrigin.Invalidated);
        dispatcher.InvalidationCount.Should().Be(1);
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
        var counters = telemetry.GetSnapshot(request.SessionId);
        counters.ReceivedComplete.Should().Be(1);
        counters.Enqueued.Should().Be(0);
        counters.Persisted.Should().Be(0);
        counters.LastMessageAt.Should().NotBeNull();
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
            new StubReadinessDispatcher(),
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
        TimeProvider? timeProvider = null,
        ICollectorRuntimeReadinessDispatcher? readinessDispatcher = null,
        ICollectorWebSocketFactory? webSocketFactory = null)
    {
        return new CollectorWebSocketWorker(
            request,
            webSocketFactory ?? new StubWebSocketFactory(connection),
            options ?? CreateOptions(),
            messageSink ?? new StubRawMarketMessageSink(),
            telemetry ?? new RawMarketMessageTelemetry(),
            readinessDispatcher ?? new StubReadinessDispatcher(),
            timeProvider ?? TimeProvider.System,
            lifetime ?? new StubHostApplicationLifetime(),
            NullLogger<CollectorWebSocketWorker>.Instance);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
            await Task.Delay(1, timeout.Token);
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
                "event-123",
                "runtime-test-event",
                "market-123",
                "runtime-test-market",
                "0xcondition",
                DateTimeOffset.Parse("2026-08-28T12:00:00Z"),
                DateTimeOffset.Parse("2026-08-28T12:05:00Z"),
                true,
                false,
                true,
                true,
                [
                    new CollectionMarketToken(
                        TokenId.Create("yes-token").Value,
                        "Yes",
                        0),
                    new CollectionMarketToken(
                        TokenId.Create("no-token").Value,
                        "No",
                        1)
                ]),
            DateTimeOffset.Parse("2026-08-28T11:59:50Z"));
    }

    private static byte[] BookMessage(string tokenId) =>
        Encoding.UTF8.GetBytes(
            $"{{\"event_type\":\"book\",\"market\":\"0xcondition\",\"asset_id\":\"{tokenId}\",\"bids\":[],\"asks\":[]}}");

    private sealed class StubWebSocketFactory(params StubWebSocketConnection[] connections)
        : ICollectorWebSocketFactory
    {
        private readonly Queue<StubWebSocketConnection> _connections = new(connections);
        public int CreateCallCount { get; private set; }

        public ICollectorWebSocketConnection Create()
        {
            CreateCallCount++;
            return _connections.Dequeue();
        }
    }

    private sealed class StubWebSocketConnection : ICollectorWebSocketConnection
    {
        private readonly Queue<StubFrame> _frames = new();
        private readonly List<byte[]> _sentMessages = [];

        public Func<CancellationToken, Task>? ConnectHandler { get; init; }
        public Func<ReadOnlyMemory<byte>, CancellationToken, Task>? SendHandler { get; init; }
        public Func<CancellationToken, Task>? CloseHandler { get; init; }
        public Func<Memory<byte>, CancellationToken,
            ValueTask<CollectorWebSocketReceiveResult>>? ReceiveHandler { get; init; }
        public Uri? Endpoint { get; private set; }
        public byte[]? SentMessage { get; private set; }
        public IReadOnlyList<byte[]> SentMessages => _sentMessages;
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
            var payload = message.ToArray();
            _sentMessages.Add(payload);
            if (SentMessage is null || !payload.SequenceEqual("PING"u8.ToArray()))
                SentMessage = payload;
            return SendHandler?.Invoke(message, cancellationToken) ?? Task.CompletedTask;
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

        public int Count
        {
            get
            {
                lock (_messages)
                {
                    return _messages.Count;
                }
            }
        }
    }

    private sealed class StubReadinessDispatcher : ICollectorRuntimeReadinessDispatcher
    {
        public int AwaitingInitialBooksCount { get; private set; }
        public int AwaitingHeartbeatCount { get; private set; }
        public int RunningCount { get; private set; }
        public int InvalidationCount { get; private set; }
        public Func<CancellationToken, Task<UnitResult<Error>>>? AwaitingInitialBooksHandler { get; init; }
        public UnitResult<Error>? RecordInitialBookResult { get; init; }
        public List<(CollectorSessionId SessionId, string TokenId, long Epoch, DateTimeOffset EnqueuedAt)>
            TokenReadinessRecords { get; } = [];
        private readonly TaskCompletionSource _running = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<UnitResult<Error>> RecordInitialBookEnqueuedAsync(
            CollectorSessionId sessionId,
            TokenId tokenId,
            long connectionEpoch,
            DateTimeOffset enqueuedAt,
            CancellationToken cancellationToken)
        {
            if (RecordInitialBookResult is { IsFailure: true })
                return Task.FromResult(RecordInitialBookResult.Value);

            TokenReadinessRecords.Add((sessionId, tokenId.Value, connectionEpoch, enqueuedAt));
            return Task.FromResult(UnitResult.Success<Error>());
        }

        public Task<UnitResult<Error>> MarkAwaitingInitialBooksAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken)
        {
            AwaitingInitialBooksCount++;
            return AwaitingInitialBooksHandler?.Invoke(cancellationToken)
                ?? Task.FromResult(UnitResult.Success<Error>());
        }

        public Task<UnitResult<Error>> MarkAwaitingHeartbeatAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken)
        {
            AwaitingHeartbeatCount++;
            return Task.FromResult(UnitResult.Success<Error>());
        }

        public Task<UnitResult<Error>> MarkRunningAsync(
            CollectorSessionId sessionId,
            DateTimeOffset subscriptionReadyAt,
            CancellationToken cancellationToken)
        {
            RunningCount++;
            _running.TrySetResult();
            return Task.FromResult(UnitResult.Success<Error>());
        }

        public async Task WaitForRunningAsync()
        {
            await _running.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }

        public Task<UnitResult<Error>> BeginInvalidationAsync(
            CollectorSessionId sessionId,
            Error failure,
            CancellationToken cancellationToken)
        {
            InvalidationCount++;
            return Task.FromResult(UnitResult.Success<Error>());
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
