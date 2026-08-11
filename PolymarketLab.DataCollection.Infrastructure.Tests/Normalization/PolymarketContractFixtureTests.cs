using FluentAssertions;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Normalization;

public sealed class PolymarketContractFixtureTests
{
    public static TheoryData<string, string, string> ObjectFixtures => new()
    {
        {
            "best-bid-ask.json",
            "best_bid_ask",
            "9eff580d0472a0824e04a850b4466ea24234b64ee7b9f7c7b2fc089b75f1e141"
        },
        {
            "book.json",
            "book",
            "3189a7d5b1e3f7cf3807a93641935649e6ee323091c795e05e6771bf18fbd396"
        },
        {
            "last-trade-price.json",
            "last_trade_price",
            "bf026ed4139096b055274f865be04399d9c326f7a8bdaa830674b623d0fafab9"
        },
        {
            "market-resolved.json",
            "market_resolved",
            "1348fc77e73e2cdda885952920591dff490d2f5e6b70cbd2d84dbb33f9764159"
        },
        {
            "new-market.json",
            "new_market",
            "bdbc4c2950bec9f5087bf76dd876aa1ec3d93cf80b2ca291b5dbd2db59487e2f"
        },
        {
            "price-change.json",
            "price_change",
            "9f0c625bcacce2414db69bc62fd3031e102f87863738490999ea5f5ac8a2b385"
        },
        {
            "tick-size-change.json",
            "tick_size_change",
            "e901a36229b95cfdb2e0f833cbab47b8631eda813154cedccd54d8a25371137d"
        }
    };

    [Theory]
    [MemberData(nameof(ObjectFixtures))]
    public void ObjectFixture_ShouldMatchArchivedPayload(
        string fileName,
        string expectedEventType,
        string expectedSha256)
    {
        var payload = ReadFixture(fileName, sourceEndsWithLineFeed: false);

        Convert.ToHexStringLower(SHA256.HashData(payload)).Should().Be(expectedSha256);
        using var document = JsonDocument.Parse(payload);
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        document.RootElement.GetProperty("event_type").GetString().Should().Be(expectedEventType);
    }

    [Fact]
    public void BookArrayFixture_ShouldContainTwoLogicalEvents()
    {
        var payload = ReadFixture("book-array.json", sourceEndsWithLineFeed: true);

        Convert.ToHexStringLower(SHA256.HashData(payload)).Should().Be(
            "89adae5461268f9d554debe580272b5b216207117d5f1d99f4355ecabc7202d9");
        using var document = JsonDocument.Parse(payload);
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        document.RootElement.GetArrayLength().Should().Be(2);
        document.RootElement.EnumerateArray()
            .Select(element => element.GetProperty("event_type").GetString())
            .Should()
            .Equal("book", "book");
    }

    [Fact]
    public void EmptyArrayFixture_ShouldPreserveObservedHeartbeatShape()
    {
        var payload = ReadFixture("empty-array.json", sourceEndsWithLineFeed: true);

        Convert.ToHexStringLower(SHA256.HashData(payload)).Should().Be(
            "37517e5f3dc66819f61f5a7bb8ace1921282415f10551d2defa5c3eb0985b570");
        using var document = JsonDocument.Parse(payload);
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        document.RootElement.GetArrayLength().Should().Be(0);
    }

    private static byte[] ReadFixture(string fileName, bool sourceEndsWithLineFeed)
    {
        var assembly = typeof(PolymarketContractFixtureTests).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith($".{fileName}", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();

        if (!sourceEndsWithLineFeed && bytes.Length > 0 && bytes[^1] == (byte)'\n')
            return bytes[..^1];

        return bytes;
    }
}
