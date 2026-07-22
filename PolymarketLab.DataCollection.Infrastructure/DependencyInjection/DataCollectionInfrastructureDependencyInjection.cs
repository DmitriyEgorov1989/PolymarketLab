using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.CollectorSession;

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

        services.AddDbContext<DataCollectionDbContext>((serviceProvider, options) =>
        {
            var databaseOptions = serviceProvider
                .GetRequiredService<IOptions<DataBaseOptions>>()
                .Value;

            options.UseNpgsql(databaseOptions.ConnectionString);
        });

        services.AddScoped<ICollectorSessionRepository, CollectorSessionRepository>();

        return services;
    }
}
