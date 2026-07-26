using CSharpFunctionalExtensions;
using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using PolymarketLab.DataCollection.Core.Application.DependencyInjection;
using PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StartCollector;
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
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(TimeProvider)
            && descriptor.Lifetime == ServiceLifetime.Singleton);
    }
}
