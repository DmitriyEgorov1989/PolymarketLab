using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime.WebSockets;
using PolymarketLab.DataCollection.Infrastructure.Adapters.MarketIntegration;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.CollectorSession;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.RawMarketMessage;
using PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;

namespace PolymarketLab.DataCollection.Infrastructure.DependencyInjection;

public static class DataCollectionInfrastructureDependencyInjection
{
    public static IServiceCollection AddDataCollectionInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<DataBaseOptions>()
            .Bind(configuration.GetSection(DataBaseOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                "Database connection string is required.")
            .ValidateOnStart();

        services.AddOptions<CollectorWebSocketOptions>()
            .Bind(configuration.GetSection(CollectorWebSocketOptions.SectionName))
            .Validate(
                options => Uri.TryCreate(
                               options.Endpoint,
                               UriKind.Absolute,
                               out var endpoint)
                           && endpoint.Scheme is "ws" or "wss",
                "Collector WebSocket endpoint must be an absolute ws or wss URI.")
            .Validate(
                options => options.ConnectTimeout > TimeSpan.Zero
                           && options.ConnectTimeout <=
                           CollectorWebSocketOptions.MaximumConnectTimeout,
                "Collector WebSocket connect timeout is outside the supported range.")
            .Validate(
                options => options.StopTimeout > TimeSpan.Zero
                           && options.StopTimeout <=
                           CollectorWebSocketOptions.MaximumStopTimeout,
                "Collector WebSocket stop timeout is outside the supported range.")
            .Validate(
                options => options.ReceiveBufferSize > 0
                           && options.ReceiveBufferSize <= options.MaximumMessageSize,
                "Collector WebSocket receive buffer size must be positive and not exceed maximum message size.")
            .Validate(
                options => options.MaximumMessageSize > 0
                           && options.MaximumMessageSize <=
                           CollectorWebSocketOptions.MaximumSupportedMessageSize,
                "Collector WebSocket maximum message size is outside the supported range.")
            .ValidateOnStart();

        services.AddOptions<RawMessageIngestionOptions>()
            .Bind(configuration.GetSection(RawMessageIngestionOptions.SectionName))
            .Validate(
                options => options.Capacity > 0,
                "Raw message ingestion capacity must be positive.")
            .Validate(
                options => options.BatchSize > 0
                           && options.BatchSize <= options.Capacity,
                "Raw message ingestion batch size must be positive and not exceed capacity.")
            .Validate(
                options => options.FlushInterval > TimeSpan.Zero
                           && options.FlushInterval <=
                           RawMessageIngestionOptions.MaximumFlushInterval,
                "Raw message ingestion flush interval is outside the supported range.")
            .Validate(
                options => options.ShutdownTimeout > TimeSpan.Zero
                           && options.ShutdownTimeout <=
                           RawMessageIngestionOptions.MaximumShutdownTimeout,
                "Raw message ingestion shutdown timeout is outside the supported range.")
            .ValidateOnStart();

        services.AddOptions<CollectorLifecycleOptions>()
            .Bind(configuration.GetSection(CollectorLifecycleOptions.SectionName))
            .Validate(
                options => options.ShutdownTimeout > TimeSpan.Zero
                           && options.ShutdownTimeout <=
                           CollectorLifecycleOptions.MaximumShutdownTimeout,
                "Collector lifecycle shutdown timeout is outside the supported range.")
            .Validate<IOptions<CollectorWebSocketOptions>>(
                (options, webSocketOptions) =>
                    options.ShutdownTimeout >= webSocketOptions.Value.StopTimeout,
                "Collector lifecycle shutdown timeout must not be shorter than the WebSocket stop timeout.")
            .ValidateOnStart();

        services.AddDbContext<DataCollectionDbContext>((serviceProvider, options) =>
        {
            var databaseOptions = serviceProvider
                .GetRequiredService<IOptions<DataBaseOptions>>()
                .Value;

            options.UseNpgsql(databaseOptions.ConnectionString);
        });

        services.AddScoped<ICollectorSessionRepository, CollectorSessionRepository>();
        services.AddScoped<IMarketCollectionSource, MarketCollectionSource>();
        services.AddScoped<IRawMarketMessageWriter, RawMarketMessageWriter>();
        services.AddSingleton<ICollectorWebSocketFactory, ClientWebSocketFactory>();
        services.AddSingleton<ICollectorWorkerFactory, CollectorWebSocketWorkerFactory>();
        services.AddSingleton<
            ICollectorRuntimeFailureDispatcher,
            CollectorRuntimeFailureDispatcher>();
        services.AddSingleton<CollectorRuntime>();
        services.AddSingleton<ICollectorRuntime>(serviceProvider =>
            serviceProvider.GetRequiredService<CollectorRuntime>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<RawMarketMessageTelemetry>();
        services.AddSingleton<RawMarketMessageChannel>();
        services.AddSingleton<IRawMarketMessageSink>(serviceProvider =>
            serviceProvider.GetRequiredService<RawMarketMessageChannel>());
        services.AddSingleton<RawMarketMessagePersistenceWorker>();
        services.AddSingleton<IRawMessagePersistenceCompletion>(serviceProvider =>
            serviceProvider.GetRequiredService<RawMarketMessagePersistenceWorker>());
        services.AddHostedService(serviceProvider =>
            serviceProvider.GetRequiredService<RawMarketMessagePersistenceWorker>());
        services.AddHostedService<CollectorRuntimeShutdownService>();
        services.AddHostedService<CollectorSessionStartupReconciliationService>();

        return services;
    }
}
