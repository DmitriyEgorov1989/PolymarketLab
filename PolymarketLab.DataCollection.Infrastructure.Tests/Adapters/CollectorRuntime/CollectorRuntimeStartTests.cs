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
        var runtime = new CollectorRuntimeAdapter(factory);
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
        var runtime = new CollectorRuntimeAdapter(factory);
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
        var runtime = new CollectorRuntimeAdapter(factory);
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
        var runtime = new CollectorRuntimeAdapter(factory);
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
        var runtime = new CollectorRuntimeAdapter(factory);
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
        var runtime = new CollectorRuntimeAdapter(factory);
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
        var runtime = new CollectorRuntimeAdapter(factory);
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
        var runtime = new CollectorRuntimeAdapter(factory);
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
        public int StartCallCount { get; private set; }
        public int StopCallCount { get; private set; }

        public Task<UnitResult<Error>> StartAsync(
            CancellationToken cancellationToken)
        {
            StartCallCount++;
            return start?.Invoke(cancellationToken)
                ?? Task.FromResult(UnitResult.Success<Error>());
        }

        public Task<UnitResult<Error>> StopAsync(
            CancellationToken cancellationToken)
        {
            StopCallCount++;
            return stop?.Invoke(cancellationToken)
                ?? Task.FromResult(UnitResult.Success<Error>());
        }
    }
}
