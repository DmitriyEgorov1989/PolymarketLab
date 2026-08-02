using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Infrastructure.DependencyInjection;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.DependencyInjection;

public sealed class CollectorLifecycleOptionsTests
{
    private const string ConnectionString =
        "Host=localhost;Database=polymarket_lab;Username=postgres;Password=postgres";

    [Fact]
    public void AddDataCollectionInfrastructure_WithoutOverrides_ShouldUseDefault()
    {
        using var provider = CreateProvider([]);

        var options = provider
            .GetRequiredService<IOptions<CollectorLifecycleOptions>>()
            .Value;

        options.ShutdownTimeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("00:00:05")]
    [InlineData("00:10:00")]
    public void AddDataCollectionInfrastructure_WithInvalidTimeout_ShouldFailValidation(
        string timeout)
    {
        using var provider = CreateProvider(new Dictionary<string, string?>
        {
            [$"{CollectorLifecycleOptions.SectionName}:ShutdownTimeout"] = timeout
        });

        var action = () => provider
            .GetRequiredService<IOptions<CollectorLifecycleOptions>>()
            .Value;

        action.Should().Throw<OptionsValidationException>();
    }

    private static ServiceProvider CreateProvider(
        Dictionary<string, string?> settings)
    {
        settings[$"{DataBaseOptions.SectionName}:ConnectionString"] =
            ConnectionString;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var services = new ServiceCollection();
        services.AddDataCollectionInfrastructure(configuration);
        return services.BuildServiceProvider();
    }
}
