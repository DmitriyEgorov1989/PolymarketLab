using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Infrastructure.DependencyInjection;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.DependencyInjection;

public sealed class CollectorWebSocketOptionsTests
{
    private const string ConnectionString =
        "Host=localhost;Database=polymarket_lab;Username=postgres;Password=postgres";

    [Fact]
    public void AddDataCollectionInfrastructure_WithoutOverrides_ShouldUseDefaults()
    {
        using var provider = CreateProvider([]);

        var options = provider
            .GetRequiredService<IOptions<CollectorWebSocketOptions>>()
            .Value;

        options.Endpoint.Should().Be(
            "wss://ws-subscriptions-clob.polymarket.com/ws/market");
        options.ConnectTimeout.Should().Be(TimeSpan.FromSeconds(10));
        options.StopTimeout.Should().Be(TimeSpan.FromSeconds(10));
        options.ReceiveBufferSize.Should().Be(16 * 1024);
        options.MaximumMessageSize.Should().Be(1024 * 1024);
        options.CustomFeatureEnabled.Should().BeTrue();
    }

    [Theory]
    [InlineData("https://example.com/ws", "00:00:10")]
    [InlineData("not-a-uri", "00:00:10")]
    [InlineData("wss://example.com/ws", "00:00:00")]
    [InlineData("wss://example.com/ws", "100.00:00:00")]
    public void AddDataCollectionInfrastructure_WithInvalidOptions_ShouldFailValidation(
        string endpoint,
        string connectTimeout)
    {
        using var provider = CreateProvider(new Dictionary<string, string?>
        {
            [$"{CollectorWebSocketOptions.SectionName}:Endpoint"] = endpoint,
            [$"{CollectorWebSocketOptions.SectionName}:ConnectTimeout"] = connectTimeout
        });

        var action = () => provider
            .GetRequiredService<IOptions<CollectorWebSocketOptions>>()
            .Value;

        action.Should().Throw<OptionsValidationException>();
    }

    [Theory]
    [InlineData("0", "1048576")]
    [InlineData("1024", "0")]
    [InlineData("2048", "1024")]
    [InlineData("1024", "16777217")]
    public void AddDataCollectionInfrastructure_WithInvalidReceiveSizes_ShouldFailValidation(
        string receiveBufferSize,
        string maximumMessageSize)
    {
        using var provider = CreateProvider(new Dictionary<string, string?>
        {
            [$"{CollectorWebSocketOptions.SectionName}:ReceiveBufferSize"] =
                receiveBufferSize,
            [$"{CollectorWebSocketOptions.SectionName}:MaximumMessageSize"] =
                maximumMessageSize
        });

        var action = () => provider
            .GetRequiredService<IOptions<CollectorWebSocketOptions>>()
            .Value;

        action.Should().Throw<OptionsValidationException>();
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("100.00:00:00")]
    public void AddDataCollectionInfrastructure_WithInvalidStopTimeout_ShouldFailValidation(
        string stopTimeout)
    {
        using var provider = CreateProvider(new Dictionary<string, string?>
        {
            [$"{CollectorWebSocketOptions.SectionName}:StopTimeout"] = stopTimeout
        });

        var action = () => provider
            .GetRequiredService<IOptions<CollectorWebSocketOptions>>()
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
