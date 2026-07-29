using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.CollectorRuntime;

public sealed class CollectorRuntimeFailureDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_WhenHandlerSucceeds_ShouldNotStopApplication()
    {
        var handler = new StubFailureHandler(UnitResult.Success<Error>());
        var lifetime = new StubHostApplicationLifetime();
        using var provider = CreateProvider(handler);
        var dispatcher = CreateDispatcher(provider, lifetime);
        var failure = CreateFailure();

        await dispatcher.DispatchAsync(failure, CancellationToken.None);

        handler.Failures.Should().ContainSingle().Which.Should().Be(failure);
        lifetime.StopCallCount.Should().Be(0);
    }

    [Fact]
    public async Task DispatchAsync_WhenHandlerFails_ShouldStopApplication()
    {
        var persistenceError = new Error(
            "collector.runtime.failure.persistence_failed",
            "Failure could not be persisted.",
            ErrorType.Failure);
        var handler = new StubFailureHandler(UnitResult.Failure(persistenceError));
        var lifetime = new StubHostApplicationLifetime();
        using var provider = CreateProvider(handler);
        var dispatcher = CreateDispatcher(provider, lifetime);

        await dispatcher.DispatchAsync(CreateFailure(), CancellationToken.None);

        lifetime.StopCallCount.Should().Be(1);
    }

    private static ServiceProvider CreateProvider(ICollectorRuntimeFailureHandler handler)
    {
        return new ServiceCollection()
            .AddScoped(_ => handler)
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true
            });
    }

    private static CollectorRuntimeFailureDispatcher CreateDispatcher(
        ServiceProvider provider,
        IHostApplicationLifetime lifetime)
    {
        return new CollectorRuntimeFailureDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            lifetime,
            NullLogger<CollectorRuntimeFailureDispatcher>.Instance);
    }

    private static CollectorRuntimeFailure CreateFailure()
    {
        return new CollectorRuntimeFailure(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero),
            new Error(
                "collector.runtime.receive.failed",
                "Receive failed.",
                ErrorType.Failure));
    }

    private sealed class StubFailureHandler(UnitResult<Error> result)
        : ICollectorRuntimeFailureHandler
    {
        public List<CollectorRuntimeFailure> Failures { get; } = [];

        public Task<UnitResult<Error>> HandleAsync(
            CollectorRuntimeFailure failure,
            CancellationToken cancellationToken)
        {
            Failures.Add(failure);
            return Task.FromResult(result);
        }
    }

    private sealed class StubHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public int StopCallCount { get; private set; }

        public void StopApplication()
        {
            StopCallCount++;
        }
    }
}
