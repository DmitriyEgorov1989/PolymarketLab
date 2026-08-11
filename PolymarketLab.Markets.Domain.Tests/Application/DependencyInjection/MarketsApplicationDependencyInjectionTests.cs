using CSharpFunctionalExtensions;
using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using PolymarketLab.Markets.Contracts;
using PolymarketLab.Markets.Core.Application.DependencyInjection;
using PolymarketLab.Markets.Core.Application.UseCases.Commands;
using PolymarketLab.SharedKernel.Mediation;
using static PolymarketLab.SharedKernel.Errors.Error;
using Xunit;

namespace PolymarketLab.Markets.Domain.Tests.Application.DependencyInjection;

public sealed class MarketsApplicationDependencyInjectionTests
{
    [Fact]
    public void AddMarketsApplication_ShouldRegisterHandlersAndValidators()
    {
        var services = new ServiceCollection();

        var result = services.AddMarketsApplication();

        result.Should().BeSameAs(services);
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IRequestHandler<
                RegisterMarketCommand,
                Result<RegisterMarketResponse, ErrorList>>));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IValidator<RegisterMarketCommand>)
            && descriptor.ImplementationType == typeof(RegisterCommandValidation));
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IPipelineBehavior<
                RegisterMarketCommand,
                Result<RegisterMarketResponse, ErrorList>>)
            && descriptor.ImplementationType == typeof(ValidationBehavior<
                RegisterMarketCommand,
                RegisterMarketResponse>));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IMarketsReader)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
    }
}
