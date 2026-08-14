using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Application.DependencyInjection;
using PolymarketLab.DataCollection.Core.Application.Normalization;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.DependencyInjection;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.DependencyInjection;

public sealed class NormalizerOptionsTests
{
    private const string ConnectionString =
        "Host=localhost;Database=polymarket_lab;Username=postgres;Password=postgres";

    [Fact]
    public void AddDataCollectionInfrastructure_WithoutOverrides_ShouldUseSafeDefaults()
    {
        using var provider = CreateProvider([]);

        var options = provider.GetRequiredService<IOptions<NormalizerOptions>>().Value;

        options.Enabled.Should().BeTrue();
        options.ProjectionVersion.Should().Be(1);
        options.BatchSize.Should().Be(500);
        options.IdleDelay.Should().Be(TimeSpan.FromMilliseconds(250));
        options.ClaimTimeout.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void AddDataCollectionInfrastructure_WithOverrides_ShouldBindAllOptionsAndAllowDisabled()
    {
        using var provider = CreateProvider(new Dictionary<string, string?>
        {
            [$"{NormalizerOptions.SectionName}:Enabled"] = "false",
            [$"{NormalizerOptions.SectionName}:ProjectionVersion"] = "2",
            [$"{NormalizerOptions.SectionName}:BatchSize"] = "25",
            [$"{NormalizerOptions.SectionName}:IdleDelay"] = "00:00:00",
            [$"{NormalizerOptions.SectionName}:ClaimTimeout"] = "00:10:00"
        });

        var options = provider.GetRequiredService<IOptions<NormalizerOptions>>().Value;

        options.Enabled.Should().BeFalse();
        options.ProjectionVersion.Should().Be(2);
        options.BatchSize.Should().Be(25);
        options.IdleDelay.Should().Be(TimeSpan.Zero);
        options.ClaimTimeout.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void AddDataCollectionInfrastructure_EnabledWithZeroIdleDelay_ShouldFailAtStartup()
    {
        using var provider = CreateProvider(new Dictionary<string, string?>
        {
            [$"{NormalizerOptions.SectionName}:Enabled"] = "true",
            [$"{NormalizerOptions.SectionName}:IdleDelay"] = "00:00:00"
        });

        var action = () => provider.GetRequiredService<IStartupValidator>().Validate();

        action.Should().Throw<OptionsValidationException>();
    }

    [Theory]
    [InlineData("ProjectionVersion", "0")]
    [InlineData("ProjectionVersion", "-1")]
    [InlineData("BatchSize", "0")]
    [InlineData("BatchSize", "-1")]
    [InlineData("IdleDelay", "-00:00:00.001")]
    [InlineData("ClaimTimeout", "00:00:00")]
    [InlineData("ClaimTimeout", "-00:00:01")]
    public void AddDataCollectionInfrastructure_WithInvalidOptions_ShouldFailAtStartup(
        string option,
        string value)
    {
        using var provider = CreateProvider(new Dictionary<string, string?>
        {
            [$"{NormalizerOptions.SectionName}:{option}"] = value
        });

        var action = () => provider.GetRequiredService<IStartupValidator>().Validate();

        action.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public async Task NormalizationProcessor_ShouldUseConfiguredClaimParameters()
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{NormalizerOptions.SectionName}:ProjectionVersion"] = "3",
            [$"{NormalizerOptions.SectionName}:BatchSize"] = "42",
            [$"{NormalizerOptions.SectionName}:ClaimTimeout"] = "00:07:00"
        };
        var configuration = CreateConfiguration(settings);
        var repository = new CapturingClaimRepository();
        var services = new ServiceCollection();
        services.AddDataCollectionApplication();
        services.AddDataCollectionInfrastructure(configuration);
        services.Replace(ServiceDescriptor.Singleton<
            IRawMessageNormalizationClaimRepository>(repository));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<INormalizationProcessor>();

        await processor.ProcessBatchAsync(default);

        repository.Request.Should().Be((3, 42, TimeSpan.FromMinutes(7)));
    }

    private static ServiceProvider CreateProvider(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddDataCollectionInfrastructure(CreateConfiguration(settings));
        return services.BuildServiceProvider();
    }

    private static IConfiguration CreateConfiguration(Dictionary<string, string?> settings)
    {
        settings[$"{DataBaseOptions.SectionName}:ConnectionString"] = ConnectionString;
        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }

    private sealed class CapturingClaimRepository : IRawMessageNormalizationClaimRepository
    {
        public (int ProjectionVersion, int BatchSize, TimeSpan ClaimTimeout)? Request { get; private set; }

        public Task<IReadOnlyList<ClaimedRawMessage>> ClaimBatchAsync(
            int projectionVersion,
            int batchSize,
            TimeSpan claimTimeout,
            CancellationToken cancellationToken)
        {
            Request = (projectionVersion, batchSize, claimTimeout);
            return Task.FromResult<IReadOnlyList<ClaimedRawMessage>>([]);
        }
    }
}
