using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.Markets.Core.Ports;
using PolymarketLab.Markets.Infrastructure.Adapters.GammaMarket;
using PolymarketLab.Markets.Infrastructure.Adapters.Postgres;
using PolymarketLab.Markets.Infrastructure.Adapters.Postgres.Repository;

namespace PolymarketLab.Markets.Infrastructure.DependencyInjection;

public static class MarketsInfrastructureDependencyInjection
{
    public static IServiceCollection AddMarketsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<DataBaseOptions>()
            .Bind(configuration.GetSection(DataBaseOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                "Database connection string is required.")
            .ValidateOnStart();

        services.AddDbContext<MarketsDbContext>((serviceProvider, options) =>
        {
            var databaseOptions = serviceProvider
                .GetRequiredService<IOptions<DataBaseOptions>>()
                .Value;

            options.UseNpgsql(databaseOptions.ConnectionString);
        });

        services.AddScoped<IMarketRepository, MarketRepository>();
        services.AddHttpClient<IExternalMarketGateway, GammaMarketClient>();

        return services;
    }
}