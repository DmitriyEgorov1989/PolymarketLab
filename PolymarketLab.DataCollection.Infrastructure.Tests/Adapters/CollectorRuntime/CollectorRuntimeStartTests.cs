using CSharpFunctionalExtensions;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;
using CollectorRuntimeAdapter = PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime.CollectorRuntime;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.CollectorRuntime;

public sealed class CollectorRuntimeStartTests
{
    [Fact]
    public async Task StartAsync_ShouldCreateAndStartWorker()
    {
        var worker = new StubCollectorWorker();
        var factory = new StubCollectorWorkerFactory(() => worker);
        var runtime = CreateRuntime(factory);
        var request = CreateRequest();

        var result = await runtime.StartAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        factory.CreateCallCount.Should().Be(1);
        factory.LastRequest.Should().BeSameAs(request);
        worker.StartCallCount.Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_WithConcurrentCalls_ShouldStartWorkerOnce()
    {
        var startResult = new TaskCompletionSource<UnitResult<Error>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new StubCollectorWorker(_ => startResult.Task);
        var factory = new StubCollectorWorkerFactory(() => worker);
        var runtime = CreateRuntime(factory);
        var request = CreateRequest();

        var firstStart = runtime.StartAsync(request, CancellationToken.None);
        var secondStart = runtime.StartAsync(request, CancellationToken.None);

        factory.CreateCallCount.Should().Be(1);
        worker.StartCallCount.Should().Be(1);

        startResult.SetResult(UnitResult.Success<Error>());
        var results = await Task.WhenAll(firstStart, secondStart);

        results.Should().OnlyContain(result => result.IsSuccess);
    }

    [Fact]
    public async Task StartAsync_AfterSuccessfulStart_ShouldReuseCompletedStart()
    {
        var worker = new StubCollectorWorker();
        var factory = new StubCollectorWorkerFactory(() => worker);
        var runtime = CreateRuntime(factory);
        var request = CreateRequest();

        var firstResult = await runtime.StartAsync(request, CancellationToken.None);
        var secondResult = await runtime.StartAsync(request, CancellationToken.None);

        firstResult.IsSuccess.Should().BeTrue();
        secondResult.IsSuccess.Should().BeTrue();
        factory.CreateCallCount.Should().Be(1);
        worker.StartCallCount.Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_WhenWorkerFails_ShouldAllowRetry()
    {
        var error = new Error(
            "collector.runtime.start.failed",
            "Collector worker failed to start.",
            ErrorType.Failure);
        var failedWorker = new StubCollectorWorker(
            _ => Task.FromResult(UnitResult.Failure(error)));
        var successfulWorker = new StubCollectorWorker();
        var factory = new StubCollectorWorkerFactory(
            () => failedWorker,
            () => successfulWorker);
        var runtime = CreateRuntime(factory);
        var request = CreateRequest();

        var failedResult = await runtime.StartAsync(request, CancellationToken.None);
        var retryResult = await runtime.StartAsync(request, CancellationToken.None);

        failedResult.IsFailure.Should().BeTrue();
        failedResult.Error.Should().Be(error);
        retryResult.IsSuccess.Should().BeTrue();
        factory.CreateCallCount.Should().Be(2);
        failedWorker.StartCallCount.Should().Be(1);
        successfulWorker.StartCallCount.Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_WhenOwnerIsCancelled_ShouldAllowRetry()
    {
        var startupEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelledWorker = new StubCollectorWorker(async cancellationToken =>
        {
            startupEntered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return UnitResult.Success<Error>();
        });
        var successfulWorker = new StubCollectorWorker();
        var factory = new StubCollectorWorkerFactory(
            () => cancelledWorker,
            () => successfulWorker);
        var runtime = CreateRuntime(factory);
        var request = CreateRequest();
        using var cancellationTokenSource = new CancellationTokenSource();

        var startTask = runtime.StartAsync(request, cancellationTokenSource.Token);
        await startupEntered.Task;
        cancellationTokenSource.Cancel();

        Func<Task> cancelledStart = async () => await startTask;
        await cancelledStart.Should().ThrowAsync<OperationCanceledException>();

        var retryResult = await runtime.StartAsync(request, CancellationToken.None);

        retryResult.IsSuccess.Should().BeTrue();
        factory.CreateCallCount.Should().Be(2);
        successfulWorker.StartCallCount.Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_WhenDuplicateWaitIsCancelled_ShouldKeepWorkerRunning()
    {
        var startResult = new TaskCompletionSource<UnitResult<Error>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new StubCollectorWorker(_ => startResult.Task);
        var factory = new StubCollectorWorkerFactory(() => worker);
        var runtime = CreateRuntime(factory);
        var request = CreateRequest();
        using var cancellationTokenSource = new CancellationTokenSource();

        var ownerStart = runtime.StartAsync(request, CancellationToken.None);
        cancellationTokenSource.Cancel();

        Func<Task> duplicateStart = async () =>
            await runtime.StartAsync(request, cancellationTokenSource.Token);
        await duplicateStart.Should().ThrowAsync<OperationCanceledException>();

        startResult.SetResult(UnitResult.Success<Error>());
        var ownerResult = await ownerStart;
        var repeatedResult = await runtime.StartAsync(request, CancellationToken.None);

        ownerResult.IsSuccess.Should().BeTrue();
        repeatedResult.IsSuccess.Should().BeTrue();
        factory.CreateCallCount.Should().Be(1);
        worker.StartCallCount.Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_WhenFactoryThrows_ShouldAllowRetry()
    {
        var successfulWorker = new StubCollectorWorker();
        var factory = new StubCollectorWorkerFactory(
            () => throw new InvalidOperationException("Factory failure."),
            () => successfulWorker);
        var runtime = CreateRuntime(factory);
        var request = CreateRequest();

        Func<Task> failedStart = async () =>
            await runtime.StartAsync(request, CancellationToken.None);
        await failedStart.Should().ThrowAsync<InvalidOperationException>();

        var retryResult = await runtime.StartAsync(request, CancellationToken.None);

        retryResult.IsSuccess.Should().BeTrue();
        factory.CreateCallCount.Should().Be(2);
        successfulWorker.StartCallCount.Should().Be(1);
    }

    [Fact]
    public async Task StopAsync_WhenWorkerStopFails_ShouldAllowNewStart()
    {
        var error = new Error(
            "collector.runtime.stop.failed",
            "Collector worker failed to stop gracefully.",
            ErrorType.Failure);
        var failedStopWorker = new StubCollectorWorker(
            stop: _ => Task.FromResult(UnitResult.Failure(error)));
        var replacementWorker = new StubCollectorWorker();
        var factory = new StubCollectorWorkerFactory(
            () => failedStopWorker,
            () => replacementWorker);
        var runtime = CreateRuntime(factory);
        var request = CreateRequest();
        await runtime.StartAsync(request, CancellationToken.None);

        var stopResult = await runtime.StopAsync(
            request.SessionId,
            CancellationToken.None);
        var restartResult = await runtime.StartAsync(
            request,
            CancellationToken.None);

        stopResult.IsFailure.Should().BeTrue();
        restartResult.IsSuccess.Should().BeTrue();
        factory.CreateCallCount.Should().Be(2);
        failedStopWorker.StopCallCount.Should().Be(1);
        replacementWorker.StartCallCount.Should().Be(1);
    }

    [Fact]
    public async Task StopAsync_WhenOwnerWaitIsCancelled_ShouldContinueSharedStop()
    {
        var stopResult = new TaskCompletionSource<UnitResult<Error>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var currentWorker = new StubCollectorWorker(
            stop: _ => stopResult.Task);
        var replacementWorker = new StubCollectorWorker();
        var factory = new StubCollectorWorkerFactory(
            () => currentWorker,
            () => replacementWorker);
        var runtime = CreateRuntime(factory);
        var request = CreateRequest();
        await runtime.StartAsync(request, CancellationToken.None);
        using var cancellationTokenSource = new CancellationTokenSource();

        var ownerStop = runtime.StopAsync(
            request.SessionId,
            cancellationTokenSource.Token);
        cancellationTokenSource.Cancel();

        Func<Task> cancelledStop = async () => await ownerStop;
        await cancelledStop.Should().ThrowAsync<OperationCanceledException>();
        currentWorker.LastStopCancellationToken.CanBeCanceled.Should().BeFalse();

        var duplicateStop = runtime.StopAsync(
            request.SessionId,
            CancellationToken.None);
        duplicateStop.IsCompleted.Should().BeFalse();
        currentWorker.StopCallCount.Should().Be(1);

        stopResult.SetResult(UnitResult.Success<Error>());
        (await duplicateStop).IsSuccess.Should().BeTrue();
        var restartResult = await runtime.StartAsync(
            request,
            CancellationToken.None);

        restartResult.IsSuccess.Should().BeTrue();
        factory.CreateCallCount.Should().Be(2);
        replacementWorker.StartCallCount.Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_DuringStop_ShouldWaitAndStartOneReplacementWorker()
    {
        var stopResult = new TaskCompletionSource<UnitResult<Error>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var currentWorker = new StubCollectorWorker(
            stop: _ => stopResult.Task);
        var replacementWorker = new StubCollectorWorker();
        var factory = new StubCollectorWorkerFactory(
            () => currentWorker,
            () => replacementWorker);
        var runtime = CreateRuntime(factory);
        var request = CreateRequest();
        await runtime.StartAsync(request, CancellationToken.None);

        var stopTask = runtime.StopAsync(
            request.SessionId,
            CancellationToken.None);
        var firstRestart = runtime.StartAsync(request, CancellationToken.None);
        var secondRestart = runtime.StartAsync(request, CancellationToken.None);

        firstRestart.IsCompleted.Should().BeFalse();
        secondRestart.IsCompleted.Should().BeFalse();
        factory.CreateCallCount.Should().Be(1);

        stopResult.SetResult(UnitResult.Success<Error>());
        var restartResults = await Task.WhenAll(firstRestart, secondRestart);

        (await stopTask).IsSuccess.Should().BeTrue();
        restartResults.Should().OnlyContain(result => result.IsSuccess);
        factory.CreateCallCount.Should().Be(2);
        currentWorker.StopCallCount.Should().Be(1);
        replacementWorker.StartCallCount.Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_WhenCancelledDuringStop_ShouldNotAffectReplacement()
    {
        var stopResult = new TaskCompletionSource<UnitResult<Error>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var currentWorker = new StubCollectorWorker(
            stop: _ => stopResult.Task);
        var replacementWorker = new StubCollectorWorker();
        var factory = new StubCollectorWorkerFactory(
            () => currentWorker,
            () => replacementWorker);
        var runtime = CreateRuntime(factory);
        var request = CreateRequest();
        await runtime.StartAsync(request, CancellationToken.None);
        using var cancellationTokenSource = new CancellationTokenSource();

        var stopTask = runtime.StopAsync(
            request.SessionId,
            CancellationToken.None);
        var cancelledRestart = runtime.StartAsync(
            request,
            cancellationTokenSource.Token);
        cancellationTokenSource.Cancel();

        Func<Task> restart = async () => await cancelledRestart;
        await restart.Should().ThrowAsync<OperationCanceledException>();
        factory.CreateCallCount.Should().Be(1);

        stopResult.SetResult(UnitResult.Success<Error>());
        (await stopTask).IsSuccess.Should().BeTrue();
        var replacementResult = await runtime.StartAsync(
            request,
            CancellationToken.None);

        replacementResult.IsSuccess.Should().BeTrue();
        factory.CreateCallCount.Should().Be(2);
        replacementWorker.StartCallCount.Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_AfterWorkerCompletes_ShouldStartReplacementWorker()
    {
        var completedWorker = new StubCollectorWorker();
        var replacementWorker = new StubCollectorWorker();
        var factory = new StubCollectorWorkerFactory(
            () => completedWorker,
            () => replacementWorker);
        var runtime = CreateRuntime(factory);
        var request = CreateRequest();
        await runtime.StartAsync(request, CancellationToken.None);

        completedWorker.Complete(UnitResult.Failure(new Error(
            "collector.runtime.receive.failed",
            "Receive failed.",
            ErrorType.Failure)));
        var restartResult = await runtime.StartAsync(
            request,
            CancellationToken.None);

        restartResult.IsSuccess.Should().BeTrue();
        factory.CreateCallCount.Should().Be(2);
        replacementWorker.StartCallCount.Should().Be(1);
    }

    [Fact]
    public async Task ShutdownAsync_ShouldStopWorkersAndRejectNewStarts()
    {
        var worker = new StubCollectorWorker();
        var factory = new StubCollectorWorkerFactory(() => worker);
        var runtime = CreateRuntime(factory);
        var request = CreateRequest();
        await runtime.StartAsync(request, CancellationToken.None);

        var shutdownResults = await runtime.ShutdownAsync(CancellationToken.None);
        var rejectedStart = await runtime.StartAsync(
            request,
            CancellationToken.None);

        worker.StopCallCount.Should().Be(1);
        shutdownResults.Should().ContainSingle();
        shutdownResults.Single().SessionId.Should().Be(request.SessionId);
        shutdownResults.Single().Result.IsSuccess.Should().BeTrue();
        rejectedStart.IsFailure.Should().BeTrue();
        rejectedStart.Error.Code.Should().Be("collector.runtime.stopping");
        factory.CreateCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Completion_WithAutonomousFailure_ShouldDispatchFailureOnce()
    {
        var error = new Error(
            "collector.runtime.receive.failed",
            "Receive failed.",
            ErrorType.Failure);
        var worker = new StubCollectorWorker();
        var factory = new StubCollectorWorkerFactory(() => worker);
        var dispatcher = new RecordingFailureDispatcher();
        var runtime = CreateRuntime(factory, dispatcher);
        var request = CreateRequest();
        await runtime.StartAsync(request, CancellationToken.None);

        worker.Complete(UnitResult.Failure(error));
        var failure = await dispatcher.Dispatched.Task.WaitAsync(TimeSpan.FromSeconds(1));

        failure.SessionId.Should().Be(request.SessionId);
        failure.Error.Should().Be(error);
        dispatcher.DispatchCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Completion_WithStartupFailure_ShouldNotDispatchFailure()
    {
        var error = new Error(
            "collector.runtime.start.failed",
            "Startup failed.",
            ErrorType.Failure);
        var worker = new StubCollectorWorker(
            _ => Task.FromResult(UnitResult.Failure(error)));
        var factory = new StubCollectorWorkerFactory(() => worker);
        var dispatcher = new RecordingFailureDispatcher();
        var runtime = CreateRuntime(factory, dispatcher);

        await runtime.StartAsync(CreateRequest(), CancellationToken.None);
        await runtime.ShutdownAsync(CancellationToken.None);

        dispatcher.DispatchCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ShutdownAsync_WithPendingFailureDispatch_ShouldWaitForObserver()
    {
        var worker = new StubCollectorWorker();
        var factory = new StubCollectorWorkerFactory(() => worker);
        var dispatcher = new BlockingFailureDispatcher();
        var runtime = CreateRuntime(factory, dispatcher);
        var request = CreateRequest();
        await runtime.StartAsync(request, CancellationToken.None);

        worker.Complete(UnitResult.Failure(new Error(
            "collector.runtime.receive.failed",
            "Receive failed.",
            ErrorType.Failure)));
        await dispatcher.Entered.Task;
        var shutdown = runtime.ShutdownAsync(CancellationToken.None);

        shutdown.IsCompleted.Should().BeFalse();
        dispatcher.Release.SetResult();
        await shutdown;
    }

    private static CollectorRuntimeAdapter CreateRuntime(
        ICollectorWorkerFactory workerFactory,
        ICollectorRuntimeFailureDispatcher? failureDispatcher = null)
    {
        return new CollectorRuntimeAdapter(
            workerFactory,
            failureDispatcher ?? new RecordingFailureDispatcher());
    }

    private static CollectorRuntimeStartRequest CreateRequest()
    {
        var market = new CollectionMarket(
            MarketId.Create(Guid.NewGuid()).Value,
            "runtime-test-market",
            [
                new CollectionMarketToken(TokenId.Create("yes-token").Value, "Yes", 0),
                new CollectionMarketToken(TokenId.Create("no-token").Value, "No", 1)
            ]);

        return new CollectorRuntimeStartRequest(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            market);
    }

    private sealed class StubCollectorWorkerFactory(
        params Func<ICollectorWorker>[] workerFactories)
        : ICollectorWorkerFactory
    {
        private readonly Queue<Func<ICollectorWorker>> _workerFactories =
            new(workerFactories);

        public int CreateCallCount { get; private set; }
        public CollectorRuntimeStartRequest? LastRequest { get; private set; }

        public ICollectorWorker Create(CollectorRuntimeStartRequest request)
        {
            CreateCallCount++;
            LastRequest = request;
            return _workerFactories.Dequeue().Invoke();
        }
    }

    private sealed class StubCollectorWorker(
        Func<CancellationToken, Task<UnitResult<Error>>>? start = null,
        Func<CancellationToken, Task<UnitResult<Error>>>? stop = null)
        : ICollectorWorker
    {
        private readonly TaskCompletionSource<CollectorWorkerCompletion> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int StartCallCount { get; private set; }
        public int StopCallCount { get; private set; }
        public CancellationToken LastStopCancellationToken { get; private set; }
        public Task<CollectorWorkerCompletion> Completion => _completion.Task;

        public async Task<UnitResult<Error>> StartAsync(
            CancellationToken cancellationToken)
        {
            StartCallCount++;
            var result = start is null
                ? UnitResult.Success<Error>()
                : await start(cancellationToken);

            if (result.IsFailure)
            {
                _completion.TrySetResult(new CollectorWorkerCompletion(
                    result,
                    CollectorWorkerCompletionOrigin.Startup,
                    DateTimeOffset.UtcNow));
            }

            return result;
        }

        public async Task<UnitResult<Error>> StopAsync(
            CancellationToken cancellationToken)
        {
            StopCallCount++;
            LastStopCancellationToken = cancellationToken;
            var result = stop is null
                ? UnitResult.Success<Error>()
                : await stop(cancellationToken);
            _completion.TrySetResult(new CollectorWorkerCompletion(
                result,
                CollectorWorkerCompletionOrigin.RequestedStop,
                DateTimeOffset.UtcNow));
            return result;
        }

        public void Complete(
            UnitResult<Error> result,
            CollectorWorkerCompletionOrigin origin =
                CollectorWorkerCompletionOrigin.Autonomous)
        {
            _completion.TrySetResult(new CollectorWorkerCompletion(
                result,
                origin,
                DateTimeOffset.UtcNow));
        }
    }

    private sealed class RecordingFailureDispatcher
        : ICollectorRuntimeFailureDispatcher
    {
        public TaskCompletionSource<CollectorRuntimeFailure> Dispatched { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DispatchCallCount { get; private set; }

        public Task<bool> DispatchAsync(
            CollectorRuntimeFailure failure,
            CancellationToken cancellationToken)
        {
            DispatchCallCount++;
            Dispatched.TrySetResult(failure);
            return Task.FromResult(true);
        }
    }

    private sealed class BlockingFailureDispatcher
        : ICollectorRuntimeFailureDispatcher
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<bool> DispatchAsync(
            CollectorRuntimeFailure failure,
            CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return true;
        }
    }
}
