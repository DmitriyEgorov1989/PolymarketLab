using CSharpFunctionalExtensions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PolymarketLab.Markets.Contracts;
using PolymarketLab.Markets.Core.Application.Integration;
using PolymarketLab.Markets.Core.Application.UseCases.Commands;
using PolymarketLab.Markets.Core.Application.UseCases.Queries.GetMarketById;
using PolymarketLab.SharedKernel.Mediation;
using ErrorList = PolymarketLab.SharedKernel.Errors.Error.ErrorList;

namespace PolymarketLab.Markets.Core.Application.DependencyInjection;

public static class MarketsApplicationDependencyInjection
{
    public static IServiceCollection AddMarketsApplication(this IServiceCollection services)
    {
        var assembly = typeof(MarketsApplicationDependencyInjection).Assembly;

        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(ServiceDescriptor.Transient<
            IPipelineBehavior<RegisterMarketCommand, Result<RegisterMarketResponse, ErrorList>>,
            ValidationBehavior<RegisterMarketCommand, RegisterMarketResponse>>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<
            IPipelineBehavior<GetMarketByIdQuery, Result<GetMarketByIdResponse, ErrorList>>,
            ValidationBehavior<GetMarketByIdQuery, GetMarketByIdResponse>>());
        services.AddScoped<IMarketsReader, MarketsReader>();

        return services;
    }
}
