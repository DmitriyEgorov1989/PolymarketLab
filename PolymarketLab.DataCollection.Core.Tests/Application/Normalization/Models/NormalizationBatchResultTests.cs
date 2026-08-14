using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.Normalization.Models;

public sealed class NormalizationBatchResultTests
{
    [Fact]
    public void Constructor_ValidCounts_ShouldCreateSummary()
    {
        var result = new NormalizationBatchResult(10, 4, 3, 2, 1, 100, 109);

        result.Total.Should().Be(10);
        result.Processed.Should().Be(4);
        result.Invalid.Should().Be(3);
        result.Unsupported.Should().Be(2);
        result.Failed.Should().Be(1);
        result.FirstRawMessageId.Should().Be(100);
        result.LastRawMessageId.Should().Be(109);
    }

    [Fact]
    public void Constructor_InconsistentCounts_ShouldRejectSummary()
    {
        var action = () => new NormalizationBatchResult(2, 1, 0, 0, 0, 1, 2);

        action.Should().Throw<ArgumentException>()
            .WithParameterName("total");
    }

    [Fact]
    public void Constructor_EmptyBatchWithRange_ShouldRejectSummary()
    {
        var action = () => new NormalizationBatchResult(0, 0, 0, 0, 0, 1, 1);

        action.Should().Throw<ArgumentException>();
    }
}
