using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PolymarketLab.Acceptance.Tests.Host;

internal sealed class AcceptanceWebApplicationFactory(
    string connectionString,
    TimeProvider timeProvider,
    Action<IServiceCollection>? configureTestServices = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:ConnectionString", connectionString);
        builder.UseSetting("RawMessageIngestion:Capacity", "32");
        builder.UseSetting("RawMessageIngestion:BatchSize", "1");
        builder.UseSetting("RawMessageIngestion:FlushInterval", "00:00:00.050");
        builder.UseSetting("Normalizer:Enabled", "true");
        builder.UseSetting("Normalizer:ProjectionVersion", "1");
        builder.UseSetting("Normalizer:BatchSize", "32");
        builder.UseSetting("Normalizer:IdleDelay", "00:00:00.050");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(timeProvider);
            configureTestServices?.Invoke(services);
        });
    }
}
