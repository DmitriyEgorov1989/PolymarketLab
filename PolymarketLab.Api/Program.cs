using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Application.DependencyInjection;
using PolymarketLab.DataCollection.Infrastructure.DependencyInjection;
using PolymarketLab.DataCollection.Presentation.Controllers;
using PolymarketLab.Framework.Response;
using PolymarketLab.Markets.Core.Application.DependencyInjection;
using PolymarketLab.Markets.Infrastructure.DependencyInjection;
using PolymarketLab.Markets.Presentation.Controllers;

const string FrontendCorsPolicy = "Frontend";

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
    .AddApplicationPart(typeof(MarketController).Assembly)
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .SelectMany(entry => entry.Value?.Errors.Select(_ =>
                    new ResponseError(
                        string.IsNullOrWhiteSpace(entry.Key)
                            ? "request.body.required"
                            : "request.validation",
                        "The request is invalid.",
                        string.IsNullOrWhiteSpace(entry.Key) ? null : entry.Key)) ?? []);

            return new BadRequestObjectResult(Envelope.Errors(errors));
        };
    });
builder.Services.AddMarketsApplication();
builder.Services.AddMarketsInfrastructure(builder.Configuration);
builder.Services.AddDataCollectionApplication();
builder.Services.AddDataCollectionInfrastructure(builder.Configuration);
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy(
    FrontendCorsPolicy,
    policy =>
    {
        if (allowedOrigins.Length > 0)
            policy.WithOrigins(allowedOrigins);

        policy.AllowAnyHeader()
            .AllowAnyMethod();
    }));
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("PolymarketLab.DataCollection.RawMessages")
        .AddMeter("PolymarketLab.DataCollection.Normalizer")
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

app.UseExceptionHandler(exceptionHandler =>
{
    exceptionHandler.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("PolymarketLab.Api.UnhandledException");
        logger.LogError(exception, "Unhandled exception while processing the HTTP request.");

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(
            Envelope.Errors(
            [
                new ResponseError(
                    "server.unexpected",
                    "An unexpected server error occurred.",
                    null)
            ]),
            context.RequestAborted);
    });
});

app.UseStatusCodePages(async statusCodeContext =>
{
    var response = statusCodeContext.HttpContext.Response;
    var statusCode = response.StatusCode;
    var error = statusCode switch
    {
        StatusCodes.Status404NotFound => new ResponseError(
            "http.not_found",
            "The requested resource was not found.",
            null),
        _ => new ResponseError(
            $"http.{statusCode}",
            "The request could not be completed.",
            null)
    };

    await response.WriteAsJsonAsync(
        Envelope.Errors([error]),
        statusCodeContext.HttpContext.RequestAborted);
});

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

app.UseCors(FrontendCorsPolicy);

app.UseAuthorization();

app.MapPrometheusScrapingEndpoint();
app.MapControllers();

app.Run();
