using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Application.DependencyInjection;
using PolymarketLab.DataCollection.Core.Application.Normalization;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime.WebSockets;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Normalization;
using PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;
using PolymarketLab.DataCollection.Infrastructure.DependencyInjection;
using PolymarketLab.Markets.Contracts;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
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
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton<IMarketsReader, StubMarketsReader>();
        services.AddSingleton<ILogger<CollectorWebSocketWorker>>(
            NullLogger<CollectorWebSocketWorker>.Instance);
        services.AddSingleton<ILogger<RawMarketMessagePersistenceWorker>>(
            NullLogger<RawMarketMessagePersistenceWorker>.Instance);
        services.AddSingleton<ILogger<CollectorRuntimeFailureDispatcher>>(
            NullLogger<CollectorRuntimeFailureDispatcher>.Instance);
        services.AddSingleton<ILogger<CollectorRuntimeShutdownService>>(
            NullLogger<CollectorRuntimeShutdownService>.Instance);
        services.AddSingleton<ILogger<CollectorSessionStartupReconciliationService>>(
            NullLogger<CollectorSessionStartupReconciliationService>.Instance);
        services.AddSingleton<ILogger<CollectorSchedulerBackgroundService>>(
            NullLogger<CollectorSchedulerBackgroundService>.Instance);
        services.AddSingleton<ILogger<CollectorSessionProgressCompletion>>(
            NullLogger<CollectorSessionProgressCompletion>.Instance);
        services.AddSingleton<ILogger<NormalizationBackgroundService>>(
            NullLogger<NormalizationBackgroundService>.Instance);
        services.AddSingleton<ILogger<NormalizationMetricsBackgroundService>>(
            NullLogger<NormalizationMetricsBackgroundService>.Instance);
        services.AddDataCollectionApplication();
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
        AssertSingleton<ICollectorSessionProgressCompletion>(firstScope, secondScope);
        AssertSingleton<ICollectorWebSocketFactory>(firstScope, secondScope);
        AssertSingleton<IRawMarketMessageSink>(firstScope, secondScope);
        AssertScoped<IRawMarketMessageWriter>(firstScope, secondScope);
        AssertScoped<ICollectorSessionProgressRepository>(firstScope, secondScope);
        AssertScoped<IRawMessageNormalizationClaimRepository>(firstScope, secondScope);
        AssertScoped<IRawMessageNormalizationReplayClaimRepository>(firstScope, secondScope);
        AssertScoped<INormalizedMessageWriter>(firstScope, secondScope);
        AssertScoped<INormalizationProcessor>(firstScope, secondScope);
        AssertScoped<INormalizationBacklogReader>(firstScope, secondScope);
        AssertTransient<IOrderBookSnapshotSource>(firstScope);
        AssertTransient<IGammaTerminalResolutionSource>(firstScope);
        AssertTransient<IClobTerminalResolutionSource>(firstScope);
        AssertScoped<DataCollectionDbContext>(firstScope, secondScope);
        AssertSingleton<NormalizerTelemetry>(firstScope, secondScope);
        AssertSingleton<IRawMessageDecoder>(firstScope, secondScope);
        AssertSingleton<INormalizationDispatcher>(firstScope, secondScope);
        AssertSingleton<INormalizationReplayService>(firstScope, secondScope);
        var firstNormalizers = firstScope.ServiceProvider
            .GetServices<IRawMessageNormalizer>()
            .ToArray();
        var secondNormalizers = secondScope.ServiceProvider
            .GetServices<IRawMessageNormalizer>()
            .ToArray();
        firstNormalizers.Select(normalizer => normalizer.GetType()).Should().Equal(
            typeof(LastTradePriceNormalizer),
            typeof(PriceChangeNormalizer),
            typeof(BookNormalizer),
            typeof(TickSizeChangeNormalizer),
            typeof(BestBidAskNormalizer),
            typeof(NewMarketNormalizer),
            typeof(MarketResolvedNormalizer));
        firstNormalizers.Should().Equal(secondNormalizers);
        provider.GetServices<IHostedService>()
            .Select(service => service.GetType())
            .Should()
            .Equal(
                typeof(RawMarketMessagePersistenceWorker),
                typeof(CollectorRuntimeShutdownService),
                typeof(CollectorSessionStartupReconciliationService),
                typeof(CollectorSchedulerBackgroundService),
                typeof(NormalizationBackgroundService),
                typeof(NormalizationMetricsBackgroundService));
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

    private static void AssertTransient<TService>(IServiceScope scope)
        where TService : notnull
    {
        var first = scope.ServiceProvider.GetRequiredService<TService>();
        var second = scope.ServiceProvider.GetRequiredService<TService>();

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
        public Task<MarketCollectionWindow?> GetCollectionWindowAsync(
            MarketId marketId,
            CancellationToken cancellationToken) =>
            Task.FromResult<MarketCollectionWindow?>(null);

        public Task<Result<MarketForCollection?, Error>> GetForCollectionAsync(
            MarketId marketId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<Result<MarketForCollection?, Error>>(
                (MarketForCollection?)null);
        }
    }
}
