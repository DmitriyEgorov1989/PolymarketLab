using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime.WebSockets;
using PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;
using PolymarketLab.DataCollection.Infrastructure.DependencyInjection;
using PolymarketLab.Markets.Contracts;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.DependencyInjection;

public sealed class DataCollectionInfrastructureDependencyInjectionTests
{
    [Fact]
    public void AddDataCollectionInfrastructure_ShouldRegisterRuntimeAsSingletons()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{DataBaseOptions.SectionName}:ConnectionString"] =
                    "Host=localhost;Database=polymarket_lab;Username=postgres;Password=postgres"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHostApplicationLifetime, StubHostApplicationLifetime>();
        services.AddSingleton<IMarketsReader, StubMarketsReader>();
        services.AddSingleton<ILogger<CollectorWebSocketWorker>>(
            NullLogger<CollectorWebSocketWorker>.Instance);
        services.AddSingleton<ILogger<RawMarketMessagePersistenceWorker>>(
            NullLogger<RawMarketMessagePersistenceWorker>.Instance);
        services.AddSingleton<ILogger<CollectorRuntimeFailureDispatcher>>(
            NullLogger<CollectorRuntimeFailureDispatcher>.Instance);
        services.AddDataCollectionInfrastructure(configuration);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        AssertSingleton<ICollectorRuntime>(firstScope, secondScope);
        AssertSingleton<ICollectorWorkerFactory>(firstScope, secondScope);
        AssertSingleton<ICollectorRuntimeFailureDispatcher>(firstScope, secondScope);
        AssertSingleton<ICollectorWebSocketFactory>(firstScope, secondScope);
        AssertSingleton<IRawMarketMessageSink>(firstScope, secondScope);
        AssertScoped<IRawMarketMessageWriter>(firstScope, secondScope);
        provider.GetServices<IHostedService>()
            .Should()
            .ContainSingle(service => service is RawMarketMessagePersistenceWorker);
        provider.GetServices<IHostedService>()
            .Should()
            .ContainSingle(service => service is CollectorRuntimeShutdownService);
    }

    private static void AssertSingleton<TService>(
        IServiceScope firstScope,
        IServiceScope secondScope)
        where TService : notnull
    {
        var first = firstScope.ServiceProvider.GetRequiredService<TService>();
        var second = secondScope.ServiceProvider.GetRequiredService<TService>();

        first.Should().BeSameAs(second);
    }

    private static void AssertScoped<TService>(
        IServiceScope firstScope,
        IServiceScope secondScope)
        where TService : notnull
    {
        var first = firstScope.ServiceProvider.GetRequiredService<TService>();
        var repeated = firstScope.ServiceProvider.GetRequiredService<TService>();
        var second = secondScope.ServiceProvider.GetRequiredService<TService>();

        first.Should().BeSameAs(repeated);
        first.Should().NotBeSameAs(second);
    }

    private sealed class StubHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }

    private sealed class StubMarketsReader : IMarketsReader
    {
        public Task<MarketForCollection?> GetForCollectionAsync(
            MarketId marketId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<MarketForCollection?>(null);
        }
    }
}
