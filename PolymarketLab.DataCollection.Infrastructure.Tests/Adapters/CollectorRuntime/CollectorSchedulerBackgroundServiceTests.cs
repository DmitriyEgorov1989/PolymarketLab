using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorScheduling;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using System.Collections.Concurrent;
using Xunit;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.CollectorRuntime;

public sealed class CollectorSchedulerBackgroundServiceTests
{
    [Fact]
    public async Task TickOnceAsync_WhenOneSessionIsBlocked_ShouldStartOtherSessionInSeparateScope()
    {
        var firstSessionId = CollectorSessionId.Create(Guid.NewGuid()).Value;
        var secondSessionId = CollectorSessionId.Create(Guid.NewGuid()).Value;
        var firstRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new StubSchedulerState([firstSessionId, secondSessionId])
        {
            OperationHandler = async (sessionId, cancellationToken) =>
            {
                if (sessionId == firstSessionId)
                    await firstRelease.Task.WaitAsync(cancellationToken);

                return UnitResult.Success<Error>();
            }
        };
        await using var provider = CreateProvider(state);
        using var service = CreateService(provider);

        await service.TickOnceAsync(CancellationToken.None);
        await WaitUntilAsync(() => state.OperationCalls.Count == 2);

        state.OperationCalls.Select(call => call.SessionId)
            .Should().BeEquivalentTo([firstSessionId, secondSessionId]);
        state.OperationCalls.Select(call => call.SchedulerId).Distinct()
            .Should().HaveCount(2);

        firstRelease.TrySetResult();
        await WaitUntilAsync(() => state.CompletedOperations == 2);
    }

    [Fact]
    public async Task TickOnceAsync_WhileSessionOperationIsRunning_ShouldNotStartDuplicate()
    {
        var sessionId = CollectorSessionId.Create(Guid.NewGuid()).Value;
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new StubSchedulerState([sessionId])
        {
            OperationHandler = async (_, cancellationToken) =>
            {
                await release.Task.WaitAsync(cancellationToken);
                return UnitResult.Success<Error>();
            }
        };
        await using var provider = CreateProvider(state);
        using var service = CreateService(provider);

        await service.TickOnceAsync(CancellationToken.None);
        await WaitUntilAsync(() => state.OperationCalls.Count == 1);
        await service.TickOnceAsync(CancellationToken.None);

        state.OperationCalls.Should().ContainSingle();

        release.TrySetResult();
        await WaitUntilAsync(() => state.CompletedOperations == 1);
        await service.TickOnceAsync(CancellationToken.None);
        await WaitUntilAsync(() => state.OperationCalls.Count == 2);
    }

    [Fact]
    public async Task TickOnceAsync_WhenDiscoveryIsCancelled_ShouldPropagateCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var state = new StubSchedulerState([])
        {
            DiscoveryException = new OperationCanceledException(cancellation.Token)
        };
        await using var provider = CreateProvider(state);
        using var service = CreateService(provider);

        var action = () => service.TickOnceAsync(cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task StopAsync_ShouldCancelAndAwaitRunningSessionOperation()
    {
        var sessionId = CollectorSessionId.Create(Guid.NewGuid()).Value;
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new StubSchedulerState([sessionId])
        {
            OperationHandler = async (_, cancellationToken) =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    cancellationObserved.TrySetResult();
                }

                return UnitResult.Success<Error>();
            }
        };
        await using var provider = CreateProvider(state);
        using var service = CreateService(provider);
        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => state.OperationCalls.Count == 1);

        await service.StopAsync(CancellationToken.None);

        cancellationObserved.Task.IsCompletedSuccessfully.Should().BeTrue();
        state.CompletedOperations.Should().Be(1);
    }

    private static ServiceProvider CreateProvider(StubSchedulerState state)
    {
        var services = new ServiceCollection();
        services.AddScoped<ICollectorScheduler>(_ => new StubScheduler(state));
        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });
    }

    private static CollectorSchedulerBackgroundService CreateService(
        IServiceProvider provider) => new(
        provider.GetRequiredService<IServiceScopeFactory>(),
        TimeProvider.System,
        NullLogger<CollectorSchedulerBackgroundService>.Instance);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
            await Task.Delay(1, timeout.Token);
    }

    private sealed class StubScheduler(StubSchedulerState state) : ICollectorScheduler
    {
        private readonly Guid _id = Guid.NewGuid();

        public Task<Result<CollectorSessionAggregate, Error>> PrepareAsync(
            CollectorSessionAggregate session,
            CollectionMarket market,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyCollection<CollectorSessionId>> GetActiveSessionIdsAsync(
            CancellationToken cancellationToken)
        {
            if (state.DiscoveryException is not null)
                return Task.FromException<IReadOnlyCollection<CollectorSessionId>>(
                    state.DiscoveryException);

            return Task.FromResult(state.SessionIds);
        }

        public async Task<UnitResult<Error>> TickSessionAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken)
        {
            state.OperationCalls.Enqueue(new OperationCall(_id, sessionId));
            var result = await state.OperationHandler(sessionId, cancellationToken);
            Interlocked.Increment(ref state.CompletedOperations);
            return result;
        }

        public Task<UnitResult<Error>> TickAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubSchedulerState(
        IReadOnlyCollection<CollectorSessionId> sessionIds)
    {
        public IReadOnlyCollection<CollectorSessionId> SessionIds { get; } = sessionIds;
        public ConcurrentQueue<OperationCall> OperationCalls { get; } = new();
        public Func<CollectorSessionId, CancellationToken, Task<UnitResult<Error>>>
            OperationHandler { get; init; } = (_, _) =>
                Task.FromResult(UnitResult.Success<Error>());
        public Exception? DiscoveryException { get; init; }
        public int CompletedOperations;
    }

    private sealed record OperationCall(
        Guid SchedulerId,
        CollectorSessionId SessionId);
}
