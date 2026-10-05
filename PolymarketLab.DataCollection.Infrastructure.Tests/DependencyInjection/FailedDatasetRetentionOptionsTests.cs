using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Infrastructure.DependencyInjection;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.DependencyInjection;

public sealed class FailedDatasetRetentionOptionsTests
{
    private const string ConnectionString =
        "Host=localhost;Database=polymarket_lab;Username=postgres;Password=postgres";

    [Fact]
    public void AddDataCollectionInfrastructure_WithoutOverrides_ShouldUseDefaults()
    {
        using var provider = CreateProvider([]);

        var options = provider
            .GetRequiredService<IOptions<FailedDatasetRetentionOptions>>()
            .Value;

        options.Enabled.Should().BeFalse();
        options.RetentionPeriod.Should().Be(TimeSpan.FromHours(12));
        options.MaximumRetainedSessions.Should().Be(5);
    }

    [Fact]
    public void AddDataCollectionInfrastructure_WithOverrides_ShouldBindValues()
    {
        using var provider = CreateProvider(new Dictionary<string, string?>
        {
            [$"{FailedDatasetRetentionOptions.SectionName}:Enabled"] = "true",
            [$"{FailedDatasetRetentionOptions.SectionName}:RetentionPeriod"] = "06:00:00",
            [$"{FailedDatasetRetentionOptions.SectionName}:MaximumRetainedSessions"] = "3"
        });

        var options = provider
            .GetRequiredService<IOptions<FailedDatasetRetentionOptions>>()
            .Value;

        options.Enabled.Should().BeTrue();
        options.RetentionPeriod.Should().Be(TimeSpan.FromHours(6));
        options.MaximumRetainedSessions.Should().Be(3);
        var policy = provider
            .GetRequiredService<IFailedDatasetRetentionPolicyProvider>()
            .Policy;
        policy.Enabled.Should().BeTrue();
        policy.RetentionPeriod.Should().Be(TimeSpan.FromHours(6));
        policy.MaximumRetainedSessions.Should().Be(3);
    }

    [Theory]
    [InlineData("00:00:00", "5")]
    [InlineData("-00:00:01", "5")]
    [InlineData("12:00:00", "0")]
    [InlineData("12:00:00", "-1")]
    public void AddDataCollectionInfrastructure_WithInvalidValues_ShouldFailValidation(
        string retentionPeriod,
        string maximumRetainedSessions)
    {
        using var provider = CreateProvider(new Dictionary<string, string?>
        {
            [$"{FailedDatasetRetentionOptions.SectionName}:RetentionPeriod"] = retentionPeriod,
            [$"{FailedDatasetRetentionOptions.SectionName}:MaximumRetainedSessions"] =
                maximumRetainedSessions
        });

        var action = () => provider
            .GetRequiredService<IOptions<FailedDatasetRetentionOptions>>()
            .Value;

        action.Should().Throw<OptionsValidationException>();
    }

    private static ServiceProvider CreateProvider(Dictionary<string, string?> settings)
    {
        settings[$"{DataBaseOptions.SectionName}:ConnectionString"] = ConnectionString;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var services = new ServiceCollection();
        services.AddDataCollectionInfrastructure(configuration);
        return services.BuildServiceProvider();
    }
}
