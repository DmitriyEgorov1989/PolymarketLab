using System.Text.Json;
using FluentAssertions;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Simulation;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Integration.Postgres;

public sealed class ExperimentDatasetFingerprintBuilderTests
{
    [Fact]
    public void Canonicalize_ShouldOrderPropertiesAndNormalizeNumbersWithoutLosingUnknownValues()
    {
        using var source = JsonDocument.Parse("""{"b":1.00,"a":null,"arr":[2.000,0,false,""]}""");

        var canonical = ExperimentDatasetFingerprintBuilder.Canonicalize(source.RootElement);

        canonical.Should().Be("""{"a":null,"arr":[2,0,false,""],"b":1}""");
    }

    [Theory]
    [InlineData("abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    public void Hash_ShouldMatchStandardSha256Vectors(string value, string expected)
    {
        ExperimentDatasetFingerprintBuilder.Hash(value).Should().Be(expected);
    }
}
