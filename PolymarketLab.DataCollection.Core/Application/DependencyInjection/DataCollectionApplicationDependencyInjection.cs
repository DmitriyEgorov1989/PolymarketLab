using CSharpFunctionalExtensions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorRuntimeFailure;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionStartupReconciliation;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionShutdown;
using PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StartCollector;
using PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StopCollector;
using PolymarketLab.DataCollection.Core.Application.UseCases.Queries.GetCollectorSessionById;
using PolymarketLab.DataCollection.Core.Application.UseCases.Queries.GetCollectorSessionByMarket;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.SharedKernel.Mediation;
using ErrorList = PolymarketLab.SharedKernel.Errors.Error.ErrorList;

namespace PolymarketLab.DataCollection.Core.Application.DependencyInjection;

public static class DataCollectionApplicationDependencyInjection
{
    public static IServiceCollection AddDataCollectionApplication(this IServiceCollection services)
    {
        var assembly = typeof(DataCollectionApplicationDependencyInjection).Assembly;

        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);
        services.TryAddEnumerable(ServiceDescriptor.Transient<
            IPipelineBehavior<StartCollectorCommand, Result<StartCollectorResponse, ErrorList>>,
            ValidationBehavior<StartCollectorCommand, StartCollectorResponse>>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<
            IPipelineBehavior<StopCollectorCommand, Result<StopCollectorResponse, ErrorList>>,
            ValidationBehavior<StopCollectorCommand, StopCollectorResponse>>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<
            IPipelineBehavior<GetCollectorSessionByIdQuery, Result<GetCollectorSessionByIdResponse, ErrorList>>,
            ValidationBehavior<GetCollectorSessionByIdQuery, GetCollectorSessionByIdResponse>>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<
            IPipelineBehavior<GetCollectorSessionByMarketQuery, Result<GetCollectorSessionByMarketResponse, ErrorList>>,
            ValidationBehavior<GetCollectorSessionByMarketQuery, GetCollectorSessionByMarketResponse>>());
        services.AddScoped<
            ICollectorRuntimeFailureHandler,
            CollectorRuntimeFailureHandler>();
        services.AddScoped<
            ICollectorSessionStartupReconciler,
            CollectorSessionStartupReconciler>();
        services.AddScoped<
            ICollectorSessionShutdownHandler,
            CollectorSessionShutdownHandler>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
