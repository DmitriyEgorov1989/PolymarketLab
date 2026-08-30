using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorScheduling;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;
using PolymarketLab.SharedKernel.Errors;
using Xunit;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.CollectorRuntime;

public sealed class CollectorSchedulerBackgroundServiceTests
{
    [Fact]
    public async Task TickOnceAsync_ShouldResolveScopedSchedulerAndForwardCancellation()
    {
        var calls = new List<TickCall>();
        var services = new ServiceCollection();
        services.AddScoped<ICollectorScheduler>(_ => new StubScheduler(calls));
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });
        var service = new CollectorSchedulerBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<CollectorSchedulerBackgroundService>.Instance);
        using var cancellationTokenSource = new CancellationTokenSource();

        await service.TickOnceAsync(cancellationTokenSource.Token);
        await service.TickOnceAsync(cancellationTokenSource.Token);

        calls.Should().HaveCount(2);
        calls.Should().OnlyContain(call =>
            call.CancellationToken == cancellationTokenSource.Token);
        calls.Select(call => call.SchedulerId).Distinct().Should().HaveCount(2);
    }

    private sealed class StubScheduler(List<TickCall> calls) : ICollectorScheduler
    {
        private readonly Guid _id = Guid.NewGuid();

        public Task<Result<CollectorSessionAggregate, Error>> PrepareAsync(
            CollectorSessionAggregate session,
            CollectionMarket market,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UnitResult<Error>> TickAsync(CancellationToken cancellationToken)
        {
            calls.Add(new TickCall(_id, cancellationToken));
            return Task.FromResult(UnitResult.Success<Error>());
        }
    }

    private sealed record TickCall(Guid SchedulerId, CancellationToken CancellationToken);
}
