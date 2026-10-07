using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorRawDatasetCompletion;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.CollectorRuntime;

public sealed class CollectorRuntimeWindowCompletionDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_WhenCompletionFailsWithoutInvalidation_ShouldStopApplication()
    {
        var session = CreateRunningSession();
        var lifetime = new StubHostApplicationLifetime();
        using var services = CreateServices(session);
        var dispatcher = new CollectorRuntimeWindowCompletionDispatcher(
            services.GetRequiredService<IServiceScopeFactory>(),
            lifetime,
            NullLogger<CollectorRuntimeWindowCompletionDispatcher>.Instance);

        await dispatcher.DispatchAsync(session.Id, CancellationToken.None);

        lifetime.StopCallCount.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DispatchAsync_WhenInvalidationIsDurable_ShouldNotStopApplication(
        bool completeInvalidation)
    {
        var session = CreateRunningSession();
        var invalidatingAt = session.CreatedAt.AddMinutes(1);
        session.BeginInvalidation(
            invalidatingAt,
            CollectorStopReason.PersistenceFailure,
            "collector.test.failure",
            "Test failure.").IsSuccess.Should().BeTrue();
        if (completeInvalidation)
        {
            session.CompleteInvalidation(invalidatingAt)
                .IsSuccess.Should().BeTrue();
        }

        var lifetime = new StubHostApplicationLifetime();
        using var services = CreateServices(session);
        var dispatcher = new CollectorRuntimeWindowCompletionDispatcher(
            services.GetRequiredService<IServiceScopeFactory>(),
            lifetime,
            NullLogger<CollectorRuntimeWindowCompletionDispatcher>.Instance);

        await dispatcher.DispatchAsync(session.Id, CancellationToken.None);

        lifetime.StopCallCount.Should().Be(0);
    }

    [Fact]
    public async Task DispatchAsync_WhenMarketCloseIsAlreadyDurable_ShouldNotStopApplication()
    {
        var session = CreateRunningSession();
        session.Stop(
            session.EventEndsAt!.Value,
            CollectorStopReason.MarketClosed).IsSuccess.Should().BeTrue();
        var lifetime = new StubHostApplicationLifetime();
        using var services = CreateServices(session);
        var dispatcher = new CollectorRuntimeWindowCompletionDispatcher(
            services.GetRequiredService<IServiceScopeFactory>(),
            lifetime,
            NullLogger<CollectorRuntimeWindowCompletionDispatcher>.Instance);

        await dispatcher.DispatchAsync(session.Id, CancellationToken.None);

        lifetime.StopCallCount.Should().Be(0);
    }

    private static ServiceProvider CreateServices(CollectorSession session)
    {
        var services = new ServiceCollection();
        services.AddScoped<ICollectorRawDatasetCompletionCoordinator>(
            _ => new FailingRawCompletionCoordinator());
        services.AddScoped<ICollectorSessionRepository>(
            _ => new SessionRepository(session));
        return services.BuildServiceProvider();
    }

    private static CollectorSession CreateRunningSession()
    {
        var createdAt = DateTimeOffset.Parse("2026-08-27T11:57:00Z");
        var session = CollectorSession.Create(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            MarketId.Create(Guid.NewGuid()).Value,
            "event-123",
            "runtime-test-event",
            "market-123",
            "runtime-test-market",
            "0xcondition",
            createdAt.AddMinutes(3),
            createdAt.AddMinutes(8),
            3,
            [
                new CollectorSessionTokenDefinition(
                    TokenId.Create("yes-token").Value,
                    "Yes",
                    0),
                new CollectorSessionTokenDefinition(
                    TokenId.Create("no-token").Value,
                    "No",
                    1)
            ],
            createdAt,
            CollectorFailurePolicy.DeleteOnFailure,
            null).Value;
        session.BeginPreparation(createdAt).IsSuccess.Should().BeTrue();
        session.MarkNewConnectionEpoch().IsSuccess.Should().BeTrue();
        session.MarkAwaitingHeartbeat().IsSuccess.Should().BeTrue();
        session.MarkRunning(createdAt.AddSeconds(1)).IsSuccess.Should().BeTrue();
        return session;
    }

    private sealed class FailingRawCompletionCoordinator
        : ICollectorRawDatasetCompletionCoordinator
    {
        public Task<UnitResult<Error>> CompleteAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(UnitResult.Failure(new Error(
                "collector.test.failure",
                "Test failure.",
                ErrorType.Failure)));
    }

    private sealed class SessionRepository(CollectorSession session)
        : ICollectorSessionRepository
    {
        public Task<CollectorSession?> GetByIdAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CollectorSession?>(session);

        public Task<CollectorSession?> GetActiveByMarketIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CollectorSession?> GetCurrentByMarketIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyCollection<CollectorSession>> GetActiveAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<CollectorSessionInsertStatus, Error>> TryAddAsync(
            CollectorSession current,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<CollectorSessionUpdateStatus, Error>> TryUpdateAsync(
            CollectorSession current,
            CollectorSessionStatus expectedStatus,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubHostApplicationLifetime : IHostApplicationLifetime
    {
        public int StopCallCount { get; private set; }
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() => StopCallCount++;
    }
}
