using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorRuntimeFailure;
using PolymarketLab.DataCollection.Core.Ports;

namespace PolymarketLab.DataCollection.Core.Application.DependencyInjection;

public static class DataCollectionApplicationDependencyInjection
{
    public static IServiceCollection AddDataCollectionApplication(this IServiceCollection services)
    {
        var assembly = typeof(DataCollectionApplicationDependencyInjection).Assembly;

        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);
        services.AddScoped<
            ICollectorRuntimeFailureHandler,
            CollectorRuntimeFailureHandler>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
