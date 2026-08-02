using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Application.DependencyInjection;
using PolymarketLab.DataCollection.Infrastructure.DependencyInjection;
using PolymarketLab.DataCollection.Presentation.Controllers;
using PolymarketLab.Markets.Core.Application.DependencyInjection;
using PolymarketLab.Markets.Infrastructure.DependencyInjection;
using PolymarketLab.Markets.Presentation.Controllers;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    options.UseUtcTimestamp = true;
});

// Add services to the container.

builder.Services
    .AddControllers()
    .AddApplicationPart(typeof(CollectorController).Assembly)
    .AddApplicationPart(typeof(MarketController).Assembly);
builder.Services.AddMarketsApplication();
builder.Services.AddMarketsInfrastructure(builder.Configuration);
builder.Services.AddDataCollectionApplication();
builder.Services.AddDataCollectionInfrastructure(builder.Configuration);
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("PolymarketLab.DataCollection.RawMessages")
        .AddPrometheusExporter());
builder.Services.AddOptions<HostOptions>()
    .Configure<
        IOptions<CollectorLifecycleOptions>,
        IOptions<RawMessageIngestionOptions>>((hostOptions, lifecycle, ingestion) =>
        {
            hostOptions.ServicesStartConcurrently = false;
            hostOptions.ServicesStopConcurrently = false;
            hostOptions.ShutdownTimeout = TimeSpan.FromTicks(checked(
                lifecycle.Value.ShutdownTimeout.Ticks * 3
                + ingestion.Value.ShutdownTimeout.Ticks
                + TimeSpan.FromSeconds(5).Ticks));
        });
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "PolymarketLab API v1");
    });
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapPrometheusScrapingEndpoint();
app.MapControllers();

app.Run();
