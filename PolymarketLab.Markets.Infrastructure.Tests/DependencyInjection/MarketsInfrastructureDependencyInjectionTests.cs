using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.Markets.Core.Ports;
using PolymarketLab.Markets.Infrastructure.Adapters.GammaMarket;
using PolymarketLab.Markets.Infrastructure.Adapters.Postgres;
using PolymarketLab.Markets.Infrastructure.Adapters.Postgres.Repository;
using PolymarketLab.Markets.Infrastructure.DependencyInjection;
using Xunit;

namespace PolymarketLab.Markets.Infrastructure.Tests.DependencyInjection;

public sealed class MarketsInfrastructureDependencyInjectionTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=polymarket_lab;Username=postgres;Password=postgres";

    [Fact]
    public void AddMarketsInfrastructure_ShouldRegisterOptionsAndAdapters()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(ConnectionString);

        var result = services.AddMarketsInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var databaseOptions = scope.ServiceProvider
            .GetRequiredService<IOptions<DataBaseOptions>>()
            .Value;
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketsDbContext>();

        result.Should().BeSameAs(services);
        databaseOptions.ConnectionString.Should().Be(ConnectionString);
        dbContext.Database.GetConnectionString().Should().Be(ConnectionString);
        scope.ServiceProvider.GetRequiredService<IMarketRepository>()
            .Should().BeOfType<MarketRepository>();
        scope.ServiceProvider.GetRequiredService<IExternalMarketGateway>()
            .Should().BeOfType<GammaMarketClient>();
    }

    [Fact]
    public void AddMarketsInfrastructure_ShouldRegisterContextAndRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        services.AddMarketsInfrastructure(CreateConfiguration(ConnectionString));
        using var provider = services.BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var firstContext = firstScope.ServiceProvider.GetRequiredService<MarketsDbContext>();
        var sameContext = firstScope.ServiceProvider.GetRequiredService<MarketsDbContext>();
        var secondContext = secondScope.ServiceProvider.GetRequiredService<MarketsDbContext>();
        var firstRepository = firstScope.ServiceProvider.GetRequiredService<IMarketRepository>();
        var sameRepository = firstScope.ServiceProvider.GetRequiredService<IMarketRepository>();

        firstContext.Should().BeSameAs(sameContext);
        firstContext.Should().NotBeSameAs(secondContext);
        firstRepository.Should().BeSameAs(sameRepository);
    }

    [Fact]
    public void AddMarketsInfrastructure_WithoutConnectionString_ShouldFailOptionsValidation()
    {
        var services = new ServiceCollection();
        services.AddMarketsInfrastructure(CreateConfiguration(null));
        using var provider = services.BuildServiceProvider();

        var action = () => provider.GetRequiredService<IOptions<DataBaseOptions>>().Value;

        action.Should().Throw<OptionsValidationException>()
            .WithMessage("*Database connection string is required.*");
    }

    private static IConfiguration CreateConfiguration(string? connectionString)
    {
        var values = new Dictionary<string, string?>
        {
            [$"{DataBaseOptions.SectionName}:ConnectionString"] = connectionString
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
