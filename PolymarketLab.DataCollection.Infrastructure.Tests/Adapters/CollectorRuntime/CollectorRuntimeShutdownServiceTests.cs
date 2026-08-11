using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionShutdown;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;
using PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;
using CollectorRuntimeAdapter = PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime.CollectorRuntime;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.CollectorRuntime;

public sealed class CollectorRuntimeShutdownServiceTests
{
    [Fact]
    public async Task StopAsync_WithSuccessfulWorker_ShouldPersistStoppingAndStopped()
    {
        var worker = new StubWorker();
        var handler = new RecordingShutdownHandler();
        var runtime = new CollectorRuntimeAdapter(
            new StubWorkerFactory(worker),
            new StubFailureDispatcher());
        var request = CreateRequest();
        await runtime.StartAsync(request, CancellationToken.None);
        using var provider = CreateProvider(handler);
        var service = new CollectorRuntimeShutdownService(
            runtime,
            new StubRawMessagePersistenceCompletion(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new CollectorLifecycleOptions()),
            NullLogger<CollectorRuntimeShutdownService>.Instance);

        await service.StopAsync(CancellationToken.None);

        worker.StopCallCount.Should().Be(1);
        handler.StoppingSessionIds.Should().Equal(request.SessionId);
        handler.StoppedSessionIds.Should().Equal(request.SessionId);
    }

    [Fact]
    public async Task StopAsync_WithFailedWorker_ShouldNotPersistStopped()
    {
        var worker = new StubWorker(UnitResult.Failure(new Error(
            "collector.runtime.stop.failed",
            "Stop failed.",
            ErrorType.Failure)));
        var handler = new RecordingShutdownHandler();
        var runtime = new CollectorRuntimeAdapter(
            new StubWorkerFactory(worker),
            new StubFailureDispatcher());
        var request = CreateRequest();
        await runtime.StartAsync(request, CancellationToken.None);
        using var provider = CreateProvider(handler);
        var service = new CollectorRuntimeShutdownService(
            runtime,
            new StubRawMessagePersistenceCompletion(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new CollectorLifecycleOptions()),
            NullLogger<CollectorRuntimeShutdownService>.Instance);

        await service.StopAsync(CancellationToken.None);

        handler.StoppingSessionIds.Should().Equal(request.SessionId);
        handler.StoppedSessionIds.Should().BeEmpty();
    }

    [Fact]
    public async Task StopAsync_WithCancelledHostToken_ShouldStillCompleteOrderedShutdown()
    {
        var worker = new StubWorker();
        var handler = new RecordingShutdownHandler();
        var runtime = new CollectorRuntimeAdapter(
            new StubWorkerFactory(worker),
            new StubFailureDispatcher());
        var request = CreateRequest();
        await runtime.StartAsync(request, CancellationToken.None);
        using var provider = CreateProvider(handler);
        var service = new CollectorRuntimeShutdownService(
            runtime,
            new StubRawMessagePersistenceCompletion(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new CollectorLifecycleOptions()),
            NullLogger<CollectorRuntimeShutdownService>.Instance);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await service.StopAsync(cancellationTokenSource.Token);

        worker.StopCallCount.Should().Be(1);
        handler.StoppedSessionIds.Should().Equal(request.SessionId);
        handler.CancellationTokens.Should().OnlyContain(token =>
            token.CanBeCanceled && !token.IsCancellationRequested);
    }

    [Fact]
    public async Task StopAsync_WhenOnePersistenceScopeFails_ShouldContinueOtherSessions()
    {
        var firstWorker = new StubWorker();
        var secondWorker = new StubWorker();
        var handler = new RecordingShutdownHandler { ThrowFirstStopping = true };
        var runtime = new CollectorRuntimeAdapter(
            new StubWorkerFactory(firstWorker, secondWorker),
            new StubFailureDispatcher());
        var firstRequest = CreateRequest();
        var secondRequest = CreateRequest();
        await runtime.StartAsync(firstRequest, CancellationToken.None);
        await runtime.StartAsync(secondRequest, CancellationToken.None);
        using var provider = CreateProvider(handler);
        var service = new CollectorRuntimeShutdownService(
            runtime,
            new StubRawMessagePersistenceCompletion(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new CollectorLifecycleOptions()),
            NullLogger<CollectorRuntimeShutdownService>.Instance);

        await service.StopAsync(CancellationToken.None);

        firstWorker.StopCallCount.Should().Be(1);
        secondWorker.StopCallCount.Should().Be(1);
        handler.StoppingSessionIds.Should().ContainSingle();
        handler.StoppedSessionIds.Should().BeEquivalentTo(
            [firstRequest.SessionId, secondRequest.SessionId]);
    }

    [Fact]
    public async Task StopAsync_WhenWorkerFailsAutonomouslyDuringShutdown_ShouldNotPersistStopped()
    {
        var worker = new StubWorker();
        var handler = new RecordingShutdownHandler
        {
            OnFirstStopping = worker.CompleteAutonomousFailure
        };
        var runtime = new CollectorRuntimeAdapter(
            new StubWorkerFactory(worker),
            new StubFailureDispatcher(false));
        var request = CreateRequest();
        await runtime.StartAsync(request, CancellationToken.None);
        using var provider = CreateProvider(handler);
        var service = new CollectorRuntimeShutdownService(
            runtime,
            new StubRawMessagePersistenceCompletion(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new CollectorLifecycleOptions()),
            NullLogger<CollectorRuntimeShutdownService>.Instance);

        await service.StopAsync(CancellationToken.None);

        handler.StoppingSessionIds.Should().Equal(request.SessionId);
        handler.StoppedSessionIds.Should().BeEmpty();
    }

    [Fact]
    public async Task StopAsync_ShouldPersistStoppedOnlyAfterPersistenceCompletion()
    {
        var worker = new StubWorker();
        var handler = new RecordingShutdownHandler();
        var persistenceCompletion = new StubRawMessagePersistenceCompletion(false);
        var runtime = new CollectorRuntimeAdapter(
            new StubWorkerFactory(worker),
            new StubFailureDispatcher());
        var request = CreateRequest();
        await runtime.StartAsync(request, CancellationToken.None);
        using var provider = CreateProvider(handler);
        var service = new CollectorRuntimeShutdownService(
            runtime,
            persistenceCompletion,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new CollectorLifecycleOptions()),
            NullLogger<CollectorRuntimeShutdownService>.Instance);

        var shutdown = service.StopAsync(CancellationToken.None);
        await persistenceCompletion.WaitUntilAwaitedAsync();

        handler.StoppedSessionIds.Should().BeEmpty();
        persistenceCompletion.CompleteSuccess();
        await shutdown;

        persistenceCompletion.CompleteProducersCallCount.Should().Be(1);
        handler.StoppedSessionIds.Should().Equal(request.SessionId);
    }

    [Fact]
    public async Task StopAsync_WhenPersistenceDrainFails_ShouldNotPersistStoppedAndShouldPersistFailed()
    {
        var worker = new StubWorker();
        var handler = new RecordingShutdownHandler();
        var persistenceCompletion = new StubRawMessagePersistenceCompletion(false);
        var runtime = new CollectorRuntimeAdapter(
            new StubWorkerFactory(worker),
            new StubFailureDispatcher());
        var request = CreateRequest();
        await runtime.StartAsync(request, CancellationToken.None);
        using var provider = CreateProvider(handler);
        var service = new CollectorRuntimeShutdownService(
            runtime,
            persistenceCompletion,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new CollectorLifecycleOptions()),
            NullLogger<CollectorRuntimeShutdownService>.Instance);

        persistenceCompletion.CompleteFailure("raw_messages.persistence.failed", 3);
        await service.StopAsync(CancellationToken.None);

        handler.StoppedSessionIds.Should().BeEmpty();
        handler.FailedSessionIds.Should().Equal(request.SessionId);
        handler.FailureErrors.Should().ContainSingle(error =>
            error.Code == "raw_messages.persistence.failed");
    }

    private static ServiceProvider CreateProvider(
        ICollectorSessionShutdownHandler handler)
    {
        return new ServiceCollection()
            .AddScoped(_ => handler)
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true
            });
    }

    private static CollectorRuntimeStartRequest CreateRequest()
    {
        return new CollectorRuntimeStartRequest(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            new CollectionMarket(
                MarketId.Create(Guid.NewGuid()).Value,
                "shutdown-test-market",
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

    private sealed class StubWorkerFactory(params ICollectorWorker[] workers)
        : ICollectorWorkerFactory
    {
        private readonly Queue<ICollectorWorker> _workers = new(workers);

        public ICollectorWorker Create(CollectorRuntimeStartRequest request) =>
            _workers.Dequeue();
    }

    private sealed class StubWorker(
        UnitResult<Error>? stopResult = null)
        : ICollectorWorker
    {
        private readonly TaskCompletionSource<CollectorWorkerCompletion> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<CollectorWorkerCompletion> Completion => _completion.Task;
        public int StopCallCount { get; private set; }

        public Task<UnitResult<Error>> StartAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult(UnitResult.Success<Error>());
        }

        public Task<UnitResult<Error>> StopAsync(
            CancellationToken cancellationToken)
        {
            StopCallCount++;
            if (_completion.Task.IsCompletedSuccessfully)
                return Task.FromResult(_completion.Task.Result.Result);

            var result = stopResult ?? UnitResult.Success<Error>();
            _completion.TrySetResult(new CollectorWorkerCompletion(
                result,
                CollectorWorkerCompletionOrigin.ApplicationShutdown,
                DateTimeOffset.UtcNow));
            return Task.FromResult(result);
        }

        public void CompleteAutonomousFailure()
        {
            var result = UnitResult.Failure(new Error(
                "collector.runtime.receive.failed",
                "Receive failed.",
                ErrorType.Failure));
            _completion.TrySetResult(new CollectorWorkerCompletion(
                result,
                CollectorWorkerCompletionOrigin.Autonomous,
                DateTimeOffset.UtcNow));
        }
    }

    private sealed class StubFailureDispatcher(bool persisted = true)
        : ICollectorRuntimeFailureDispatcher
    {
        public Task<bool> DispatchAsync(
            CollectorRuntimeFailure failure,
            CancellationToken cancellationToken) => Task.FromResult(persisted);
    }

    private sealed class StubRawMessagePersistenceCompletion
        : IRawMessagePersistenceCompletion
    {
        private readonly TaskCompletionSource<RawMessagePersistenceCompletionResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _awaited =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CompleteProducersCallCount { get; private set; }
        public Task<RawMessagePersistenceCompletionResult> Completion => _completion.Task;

        public StubRawMessagePersistenceCompletion(bool complete = true)
        {
            if (complete)
                CompleteSuccess();
        }

        public void CompleteProducers()
        {
            CompleteProducersCallCount++;
        }

        public async Task<RawMessagePersistenceCompletionResult> WaitForCompletionAsync(
            CancellationToken cancellationToken)
        {
            _awaited.TrySetResult();
            return await _completion.Task.WaitAsync(cancellationToken);
        }

        public Task WaitUntilAwaitedAsync() =>
            _awaited.Task.WaitAsync(TimeSpan.FromSeconds(2));

        public void CompleteSuccess()
        {
            _completion.TrySetResult(RawMessagePersistenceCompletionResult.Success(0));
        }

        public void CompleteFailure(string errorCode, int unconfirmedMessageCount)
        {
            _completion.TrySetResult(RawMessagePersistenceCompletionResult.Failure(
                new Error(errorCode, "Persistence failed.", ErrorType.Failure),
                unconfirmedMessageCount));
        }
    }

    private sealed class RecordingShutdownHandler
        : ICollectorSessionShutdownHandler
    {
        public List<CollectorSessionId> StoppingSessionIds { get; } = [];
        public List<CollectorSessionId> StoppedSessionIds { get; } = [];
        public List<CollectorSessionId> FailedSessionIds { get; } = [];
        public List<Error> FailureErrors { get; } = [];
        public List<CancellationToken> CancellationTokens { get; } = [];
        public bool ThrowFirstStopping { get; init; }
        public Action? OnFirstStopping { get; init; }
        private bool _stoppingExceptionThrown;
        private bool _firstStoppingHandled;

        public Task<UnitResult<Error>> MarkStoppingAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken)
        {
            CancellationTokens.Add(cancellationToken);
            if (!_firstStoppingHandled)
            {
                _firstStoppingHandled = true;
                OnFirstStopping?.Invoke();
            }

            if (ThrowFirstStopping && !_stoppingExceptionThrown)
            {
                _stoppingExceptionThrown = true;
                throw new InvalidOperationException("Persistence failed.");
            }

            StoppingSessionIds.Add(sessionId);
            return Task.FromResult(UnitResult.Success<Error>());
        }

        public Task<UnitResult<Error>> MarkStoppedAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken)
        {
            StoppedSessionIds.Add(sessionId);
            CancellationTokens.Add(cancellationToken);
            return Task.FromResult(UnitResult.Success<Error>());
        }

        public Task<UnitResult<Error>> MarkFailedAsync(
            CollectorSessionId sessionId,
            Error error,
            CancellationToken cancellationToken)
        {
            FailedSessionIds.Add(sessionId);
            FailureErrors.Add(error);
            CancellationTokens.Add(cancellationToken);
            return Task.FromResult(UnitResult.Success<Error>());
        }
    }
}
