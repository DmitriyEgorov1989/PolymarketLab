using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorRuntimeReadiness;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.CollectorRuntime;

public sealed class CollectorRuntimeReadinessDispatcherTests
{
    [Theory]
    [InlineData("InitialBooks")]
    [InlineData("Heartbeat")]
    [InlineData("Running")]
    [InlineData("Invalidation")]
    [InlineData("BookEnqueued")]
    public async Task DispatchAsync_WhenCallerCancels_ShouldPropagateCancellationWithoutStoppingApplication(
        string operation)
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new StubReadinessHandler(token =>
        {
            token.Should().Be(cancellation.Token);
            cancellation.Cancel();
            return Task.FromCanceled<UnitResult<Error>>(token);
        });
        var lifetime = new StubHostApplicationLifetime();
        using var provider = CreateProvider(handler);
        var dispatcher = CreateDispatcher(provider, lifetime);
        var sessionId = CollectorSessionId.Create(Guid.NewGuid()).Value;
        var timestamp = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

        Func<Task> action = () => operation switch
        {
            "InitialBooks" => dispatcher.MarkAwaitingInitialBooksAsync(sessionId, cancellation.Token),
            "Heartbeat" => dispatcher.MarkAwaitingHeartbeatAsync(sessionId, cancellation.Token),
            "Running" => dispatcher.MarkRunningAsync(sessionId, timestamp, cancellation.Token),
            "Invalidation" => dispatcher.BeginInvalidationAsync(
                sessionId, CollectorRuntimeErrors.ReadinessPersistenceFailed(sessionId), cancellation.Token),
            "BookEnqueued" => dispatcher.RecordInitialBookEnqueuedAsync(
                sessionId, TokenId.Create("123").Value, 1, timestamp, cancellation.Token),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        var exception = await action.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cancellation.Token);
        lifetime.StopCallCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DispatchAsync_WhenPersistenceFails_ShouldStopApplicationEvenIfCallerCancelled(
        bool throwsException,
        bool callerCancelled)
    {
        using var cancellation = new CancellationTokenSource();
        var sessionId = CollectorSessionId.Create(Guid.NewGuid()).Value;
        var persistenceError = new Error("test.persistence.failed", "Persistence failed.", ErrorType.Failure);
        var handler = new StubReadinessHandler(_ =>
        {
            if (callerCancelled)
                cancellation.Cancel();

            return throwsException
                ? Task.FromException<UnitResult<Error>>(new InvalidOperationException("Persistence failed."))
                : Task.FromResult(UnitResult.Failure(persistenceError));
        });
        var lifetime = new StubHostApplicationLifetime();
        using var provider = CreateProvider(handler);
        var dispatcher = CreateDispatcher(provider, lifetime);

        var result = await dispatcher.MarkAwaitingInitialBooksAsync(sessionId, cancellation.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(throwsException
            ? CollectorRuntimeErrors.ReadinessPersistenceFailed(sessionId)
            : persistenceError);
        lifetime.StopCallCount.Should().Be(1);
    }

    [Fact]
    public async Task DispatchAsync_WhenCancellationIsNotRequested_ShouldTreatCancellationExceptionAsFault()
    {
        var handler = new StubReadinessHandler(_ =>
            Task.FromException<UnitResult<Error>>(new OperationCanceledException()));
        var lifetime = new StubHostApplicationLifetime();
        using var provider = CreateProvider(handler);
        var dispatcher = CreateDispatcher(provider, lifetime);
        var sessionId = CollectorSessionId.Create(Guid.NewGuid()).Value;

        var result = await dispatcher.MarkAwaitingInitialBooksAsync(sessionId, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CollectorRuntimeErrors.ReadinessPersistenceFailed(sessionId));
        lifetime.StopCallCount.Should().Be(1);
    }

    [Fact]
    public async Task DispatchAsync_WhenHandlerSucceeds_ShouldNotStopApplication()
    {
        var handler = new StubReadinessHandler(_ => Task.FromResult(UnitResult.Success<Error>()));
        var lifetime = new StubHostApplicationLifetime();
        using var provider = CreateProvider(handler);
        var dispatcher = CreateDispatcher(provider, lifetime);

        var result = await dispatcher.MarkAwaitingInitialBooksAsync(
            CollectorSessionId.Create(Guid.NewGuid()).Value, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        lifetime.StopCallCount.Should().Be(0);
    }

    private static ServiceProvider CreateProvider(ICollectorRuntimeReadinessHandler handler) =>
        new ServiceCollection()
            .AddScoped(_ => handler)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

    private static CollectorRuntimeReadinessDispatcher CreateDispatcher(
        ServiceProvider provider,
        IHostApplicationLifetime lifetime) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), lifetime,
            NullLogger<CollectorRuntimeReadinessDispatcher>.Instance);

    private sealed class StubReadinessHandler(Func<CancellationToken, Task<UnitResult<Error>>> action)
        : ICollectorRuntimeReadinessHandler
    {
        public Task<UnitResult<Error>> MarkAwaitingInitialBooksAsync(
            CollectorSessionId sessionId, CancellationToken cancellationToken) => action(cancellationToken);

        public Task<UnitResult<Error>> MarkAwaitingHeartbeatAsync(
            CollectorSessionId sessionId, CancellationToken cancellationToken) => action(cancellationToken);

        public Task<UnitResult<Error>> MarkRunningAsync(
            CollectorSessionId sessionId, DateTimeOffset subscriptionReadyAt,
            CancellationToken cancellationToken) => action(cancellationToken);

        public Task<UnitResult<Error>> BeginInvalidationAsync(
            CollectorSessionId sessionId, Error failure,
            CancellationToken cancellationToken) => action(cancellationToken);

        public Task<UnitResult<Error>> RecordInitialBookEnqueuedAsync(
            CollectorSessionId sessionId, TokenId tokenId, long connectionEpoch,
            DateTimeOffset enqueuedAt, CancellationToken cancellationToken) => action(cancellationToken);
    }

    private sealed class StubHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public int StopCallCount { get; private set; }

        public void StopApplication() => StopCallCount++;
    }
}
