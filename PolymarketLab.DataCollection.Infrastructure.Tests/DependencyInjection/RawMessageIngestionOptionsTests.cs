using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Infrastructure.DependencyInjection;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.DependencyInjection;

public sealed class RawMessageIngestionOptionsTests
{
    private const string ConnectionString =
        "Host=localhost;Database=polymarket_lab;Username=postgres;Password=postgres";

    [Fact]
    public void AddDataCollectionInfrastructure_WithoutOverrides_ShouldUseDefaults()
    {
        using var provider = CreateProvider([]);

        var options = provider
            .GetRequiredService<IOptions<RawMessageIngestionOptions>>()
            .Value;

        options.Capacity.Should().Be(10_000);
        options.BatchSize.Should().Be(500);
        options.FlushInterval.Should().Be(TimeSpan.FromMilliseconds(250));
        options.ShutdownTimeout.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData("Capacity", "0")]
    [InlineData("BatchSize", "0")]
    [InlineData("BatchSize", "10001")]
    [InlineData("FlushInterval", "00:00:00")]
    [InlineData("FlushInterval", "100.00:00:00")]
    [InlineData("ShutdownTimeout", "00:00:00")]
    [InlineData("ShutdownTimeout", "100.00:00:00")]
    public void AddDataCollectionInfrastructure_WithInvalidOptions_ShouldFailValidation(
        string option,
        string value)
    {
        using var provider = CreateProvider(new Dictionary<string, string?>
        {
            [$"{RawMessageIngestionOptions.SectionName}:{option}"] = value
        });

        var action = () => provider
            .GetRequiredService<IOptions<RawMessageIngestionOptions>>()
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
