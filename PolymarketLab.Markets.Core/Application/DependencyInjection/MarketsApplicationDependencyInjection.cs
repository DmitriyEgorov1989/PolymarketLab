using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using PolymarketLab.Markets.Contracts;
using PolymarketLab.Markets.Core.Application.Integration;

namespace PolymarketLab.Markets.Core.Application.DependencyInjection;

public static class MarketsApplicationDependencyInjection
{
    public static IServiceCollection AddMarketsApplication(this IServiceCollection services)
    {
        var assembly = typeof(MarketsApplicationDependencyInjection).Assembly;

        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);
        services.AddScoped<IMarketsReader, MarketsReader>();

        return services;
    }
}
