using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;
using PolymarketLab.SharedKernel.Errors;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.CollectorRuntime;

public sealed class CollectorSessionStartupReconciliationServiceTests
{
    [Fact]
    public async Task StartAsync_ShouldResolveScopedReconcilerAndWaitForCompletion()
    {
        var reconciler = new StubReconciler(UnitResult.Success<Error>());
        using var provider = CreateProvider(reconciler);
        var service = CreateService(provider);
        using var cancellationTokenSource = new CancellationTokenSource();

        await service.StartAsync(cancellationTokenSource.Token);

        reconciler.CallCount.Should().Be(1);
        reconciler.CancellationToken.Should().Be(cancellationTokenSource.Token);
    }

    [Fact]
    public async Task StartAsync_WhenReconciliationFails_ShouldRejectApplicationStart()
    {
        var error = new Error(
            "collector.session.reconciliation.failed",
            "Reconciliation failed.",
            ErrorType.Failure);
        var reconciler = new StubReconciler(UnitResult.Failure(error));
        using var provider = CreateProvider(reconciler);
        var service = CreateService(provider);

        var action = () => service.StartAsync(CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{error.Code}*");
    }

    private static ServiceProvider CreateProvider(
        ICollectorSessionStartupReconciler reconciler)
    {
        return new ServiceCollection()
            .AddScoped(_ => reconciler)
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true
            });
    }

    private static CollectorSessionStartupReconciliationService CreateService(
        ServiceProvider provider)
    {
        return new CollectorSessionStartupReconciliationService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<CollectorSessionStartupReconciliationService>.Instance);
    }

    private sealed class StubReconciler(UnitResult<Error> result)
        : ICollectorSessionStartupReconciler
    {
        public int CallCount { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<UnitResult<Error>> ReconcileAsync(
            CancellationToken cancellationToken)
        {
            CallCount++;
            CancellationToken = cancellationToken;
            return Task.FromResult(result);
        }
    }
}
