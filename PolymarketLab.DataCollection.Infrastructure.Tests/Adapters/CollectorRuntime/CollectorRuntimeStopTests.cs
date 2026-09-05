using CSharpFunctionalExtensions;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;
using CollectorRuntimeAdapter = PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime.CollectorRuntime;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.CollectorRuntime;

public sealed class CollectorRuntimeStopTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopAsync_WhenFencedStopFails_ShouldRequireCompletionBeforeRetrySucceeds(bool throws)
    {
        var request = CreateRequest();
        var error = CollectorRuntimeErrors.StopTimedOut(request.SessionId, TimeSpan.FromSeconds(10));
        var exception = new InvalidOperationException("Stop failed.");
        var worker = new StubWorker(() => throws
            ? throw exception
            : Task.FromResult(UnitResult.Failure(error)));
        var runtime = CreateRuntime(new StubFactory(() => worker));
        await runtime.StartAsync(request, CancellationToken.None);
        runtime.FenceSession(request.SessionId);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (throws)
            {
                Func<Task> stop = () => runtime.StopAsync(request.SessionId, CancellationToken.None);
                (await stop.Should().ThrowAsync<InvalidOperationException>())
                    .Which.Should().BeSameAs(exception);
            }
            else
            {
                var result = await runtime.StopAsync(request.SessionId, CancellationToken.None);
                result.IsFailure.Should().BeTrue();
                result.Error.Should().Be(error);
            }
            worker.Completion.IsCompleted.Should().BeFalse();
        }

        worker.Complete(UnitResult.Failure(error));

        (await runtime.StopAsync(request.SessionId, CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await runtime.StopAsync(request.SessionId, CancellationToken.None)).IsSuccess.Should().BeTrue();
        worker.StopCallCount.Should().Be(1);
        (await runtime.StartAsync(request, CancellationToken.None)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_WhenCompletionPrecedesStopFailure_ShouldPreserveFirstFailure()
    {
        var request = CreateRequest();
        var error = CollectorRuntimeErrors.StopTimedOut(request.SessionId, TimeSpan.FromSeconds(10));
        var stopResult = new TaskCompletionSource<UnitResult<Error>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new StubWorker(() => stopResult.Task);
        var runtime = CreateRuntime(new StubFactory(() => worker));
        await runtime.StartAsync(request, CancellationToken.None);
        runtime.FenceSession(request.SessionId);

        var firstStop = runtime.StopAsync(request.SessionId, CancellationToken.None);
        worker.Complete(UnitResult.Failure(error));
        stopResult.SetResult(UnitResult.Failure(error));

        var firstResult = await firstStop;
        firstResult.IsFailure.Should().BeTrue();
        firstResult.Error.Should().Be(error);
        (await runtime.StopAsync(request.SessionId, CancellationToken.None)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_WhenFencedWorkerReportsSuccessBeforeCompletion_ShouldNotAuthorizeCleanup()
    {
        var request = CreateRequest();
        var worker = new StubWorker(() => Task.FromResult(UnitResult.Success<Error>()));
        var runtime = CreateRuntime(new StubFactory(() => worker));
        await runtime.StartAsync(request, CancellationToken.None);
        runtime.FenceSession(request.SessionId);

        (await runtime.StopAsync(request.SessionId, CancellationToken.None)).IsFailure.Should().BeTrue();
        (await runtime.StopAsync(request.SessionId, CancellationToken.None)).IsFailure.Should().BeTrue();

        worker.Complete(UnitResult.Success<Error>());
        (await runtime.StopAsync(request.SessionId, CancellationToken.None)).IsSuccess.Should().BeTrue();
        worker.StopCallCount.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartAsync_WhenFencedDuringWorkerCreation_ShouldObserveStopBeforeStartCompletion(bool throws)
    {
        var request = CreateRequest();
        var error = CollectorRuntimeErrors.StopTimedOut(request.SessionId, TimeSpan.FromSeconds(10));
        var worker = new StubWorker(() => throws
            ? throw new InvalidOperationException("Stop failed.")
            : Task.FromResult(UnitResult.Failure(error)));
        CollectorRuntimeAdapter runtime = null!;
        runtime = CreateRuntime(new StubFactory(() =>
        {
            runtime.FenceSession(request.SessionId);
            return worker;
        }));

        if (throws)
        {
            Func<Task> start = () => runtime.StartAsync(request, CancellationToken.None);
            await start.Should().ThrowAsync<InvalidOperationException>();
            Func<Task> stop = () => runtime.StopAsync(request.SessionId, CancellationToken.None);
            await stop.Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            (await runtime.StartAsync(request, CancellationToken.None)).IsFailure.Should().BeTrue();
            (await runtime.StopAsync(request.SessionId, CancellationToken.None)).IsFailure.Should().BeTrue();
        }

        worker.StartCallCount.Should().Be(0);
        runtime.BeginShutdown().Should().ContainSingle();
        worker.Complete(UnitResult.Failure(error));
        await runtime.ShutdownAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        runtime.BeginShutdown().Should().BeEmpty();
        (await runtime.StopAsync(request.SessionId, CancellationToken.None)).IsSuccess.Should().BeTrue();
        worker.StopCallCount.Should().Be(1);
    }

    [Fact]
    public async Task StopAsync_WhenUnfencedStopFailsBeforeCompletion_ShouldStillAllowReplacement()
    {
        var request = CreateRequest();
        var error = CollectorRuntimeErrors.StopTimedOut(request.SessionId, TimeSpan.FromSeconds(10));
        var worker = new StubWorker(() => Task.FromResult(UnitResult.Failure(error)));
        var replacement = new StubWorker(() => Task.FromResult(UnitResult.Success<Error>()));
        var workers = new Queue<ICollectorWorker>([worker, replacement]);
        var runtime = CreateRuntime(new StubFactory(workers.Dequeue));
        await runtime.StartAsync(request, CancellationToken.None);

        (await runtime.StopAsync(request.SessionId, CancellationToken.None)).IsFailure.Should().BeTrue();
        worker.Completion.IsCompleted.Should().BeFalse();
        (await runtime.StartAsync(request, CancellationToken.None)).IsSuccess.Should().BeTrue();
        replacement.StartCallCount.Should().Be(1);

        worker.Complete(UnitResult.Failure(error));
        replacement.Complete(UnitResult.Success<Error>());
    }

    private static CollectorRuntimeAdapter CreateRuntime(ICollectorWorkerFactory factory) =>
        new(factory, new StubFailureDispatcher());

    private static CollectorRuntimeStartRequest CreateRequest()
    {
        var market = new CollectionMarket(
            MarketId.Create(Guid.NewGuid()).Value,
            "event-123", "runtime-test-event", "market-123", "runtime-test-market", "0xcondition",
            DateTimeOffset.Parse("2026-08-28T12:00:00Z"),
            DateTimeOffset.Parse("2026-08-28T12:05:00Z"),
            true, false, true, true,
            [
                new CollectionMarketToken(TokenId.Create("yes-token").Value, "Yes", 0),
                new CollectionMarketToken(TokenId.Create("no-token").Value, "No", 1)
            ]);
        return new CollectorRuntimeStartRequest(
            CollectorSessionId.Create(Guid.NewGuid()).Value, market, market.EventStartsAt.AddSeconds(-10));
    }

    private sealed class StubFactory(Func<ICollectorWorker> create) : ICollectorWorkerFactory
    {
        public ICollectorWorker Create(CollectorRuntimeStartRequest request) => create();
    }

    private sealed class StubWorker(Func<Task<UnitResult<Error>>> stop) : ICollectorWorker
    {
        private readonly TaskCompletionSource<CollectorWorkerCompletion> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<CollectorWorkerCompletion> Completion => _completion.Task;
        public int StartCallCount { get; private set; }
        public int StopCallCount { get; private set; }

        public Task<UnitResult<Error>> StartAsync(CancellationToken cancellationToken)
        {
            StartCallCount++;
            return Task.FromResult(UnitResult.Success<Error>());
        }

        public Task<UnitResult<Error>> StopAsync(CancellationToken cancellationToken)
        {
            StopCallCount++;
            return stop();
        }

        public void Complete(UnitResult<Error> result) => _completion.SetResult(
            new CollectorWorkerCompletion(result, CollectorWorkerCompletionOrigin.RequestedStop, DateTimeOffset.UtcNow));
    }

    private sealed class StubFailureDispatcher : ICollectorRuntimeFailureDispatcher
    {
        public Task<bool> DispatchAsync(CollectorRuntimeFailure failure, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }
}
