using CSharpFunctionalExtensions;
using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using PolymarketLab.DataCollection.Core.Application.DependencyInjection;
using PolymarketLab.DataCollection.Core.Application.Normalization;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Resynchronization;
using PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StartCollector;
using PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StopCollector;
using PolymarketLab.DataCollection.Core.Application.UseCases.Commands.ReplayNormalization;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorRuntimeFailure;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorScheduling;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionShutdown;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionStartupReconciliation;
using PolymarketLab.DataCollection.Core.Application.UseCases.Queries.GetCollectorSessionById;
using PolymarketLab.DataCollection.Core.Application.UseCases.Queries.GetCollectorSessionByMarket;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.SharedKernel.Mediation;
using static PolymarketLab.SharedKernel.Errors.Error;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.DependencyInjection;

public sealed class DataCollectionApplicationDependencyInjectionTests
{
    [Fact]
    public void AddDataCollectionApplication_ShouldRegisterApplicationServices()
    {
        var services = new ServiceCollection();

        var result = services.AddDataCollectionApplication();

        result.Should().BeSameAs(services);
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IRequestHandler<
                StartCollectorCommand,
                Result<StartCollectorResponse, ErrorList>>));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IValidator<StartCollectorCommand>)
            && descriptor.ImplementationType == typeof(StartCollectorValidator));
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IPipelineBehavior<
                StartCollectorCommand,
                Result<StartCollectorResponse, ErrorList>>)
            && descriptor.ImplementationType == typeof(ValidationBehavior<
                StartCollectorCommand,
                StartCollectorResponse>));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IRequestHandler<
                StopCollectorCommand,
                Result<StopCollectorResponse, ErrorList>>));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IValidator<StopCollectorCommand>)
            && descriptor.ImplementationType == typeof(StopCollectorValidator));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IRequestHandler<
                GetCollectorSessionByIdQuery,
                Result<GetCollectorSessionByIdResponse, ErrorList>>));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IValidator<GetCollectorSessionByIdQuery>)
            && descriptor.ImplementationType == typeof(GetCollectorSessionByIdValidator));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IRequestHandler<
                GetCollectorSessionByMarketQuery,
                Result<GetCollectorSessionByMarketResponse, ErrorList>>));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IValidator<GetCollectorSessionByMarketQuery>)
            && descriptor.ImplementationType == typeof(GetCollectorSessionByMarketValidator));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(TimeProvider)
            && descriptor.Lifetime == ServiceLifetime.Singleton);
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(ICollectorRuntimeFailureHandler)
            && descriptor.ImplementationType == typeof(CollectorRuntimeFailureHandler)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(ICollectorSessionStartupReconciler)
            && descriptor.ImplementationType == typeof(CollectorSessionStartupReconciler)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(ICollectorSessionShutdownHandler)
            && descriptor.ImplementationType == typeof(CollectorSessionShutdownHandler)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(ICollectorScheduler)
            && descriptor.ImplementationType == typeof(CollectorScheduler)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(CollectorBoundaryCheckRegistry)
            && descriptor.Lifetime == ServiceLifetime.Singleton);
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(INormalizationDispatcher)
            && descriptor.ImplementationType == typeof(NormalizationDispatcher)
            && descriptor.Lifetime == ServiceLifetime.Singleton);
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IOrderBookProjector)
            && descriptor.ImplementationType == typeof(OrderBookProjector)
            && descriptor.Lifetime == ServiceLifetime.Singleton);
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IOrderBookStateRegistry)
            && descriptor.ImplementationType == typeof(OrderBookStateRegistry)
            && descriptor.Lifetime == ServiceLifetime.Singleton);
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IOrderBookResynchronizer)
            && descriptor.ImplementationType == typeof(OrderBookResynchronizer)
            && descriptor.Lifetime == ServiceLifetime.Transient);
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IPipelineBehavior<
                ReplayNormalizationCommand,
                Result<ReplayNormalizationResponse, ErrorList>>)
            && descriptor.ImplementationType == typeof(ValidationBehavior<
                ReplayNormalizationCommand,
                ReplayNormalizationResponse>));
    }
}
