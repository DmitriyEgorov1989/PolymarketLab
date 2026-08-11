using System.Text;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Normalization;

public sealed class RawMessageDecoderTests
{
    private readonly RawMessageDecoder _decoder = new();

    [Fact]
    public void Decode_Object_ShouldReturnSingleClonedItemWithZeroIndex()
    {
        var message = CreateMessage("""
            {
              "event_type": "book",
              "extra": { "values": [1, 2] }
            }
            """);

        var result = _decoder.Decode(message);

        result.IsDecoded.Should().BeTrue();
        result.Issue.Should().BeNull();
        var item = result.Items.Should().ContainSingle().Subject;
        item.RawItemIndex.Should().Be(0);
        item.IsDecoded.Should().BeTrue();
        item.Issue.Should().BeNull();
        item.Json.Should().NotBeNull();
        item.Json!.Value.GetProperty("event_type").GetString().Should().Be("book");
        item.Json.Value.GetProperty("extra").GetProperty("values").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void Decode_ObjectWithoutEventType_ShouldRemainValidForDispatcher()
    {
        var result = _decoder.Decode(CreateMessage("""{"question":"Will it happen?"}"""));

        var item = result.Items.Should().ContainSingle().Subject;
        result.IsDecoded.Should().BeTrue();
        item.IsDecoded.Should().BeTrue();
        item.Json!.Value.TryGetProperty("event_type", out _).Should().BeFalse();
    }

    [Fact]
    public void Decode_BookArrayFixture_ShouldPreserveLogicalEventOrderAndIndexes()
    {
        var result = _decoder.Decode(CreateMessage(ReadFixture("book-array.json")));

        var items = result.Items.ToArray();
        result.IsDecoded.Should().BeTrue();
        items.Should().HaveCount(2);
        items.Select(item => item.RawItemIndex).Should().Equal(0, 1);
        items.Should().OnlyContain(item => item.IsDecoded);
        items.Select(item => item.Json!.Value.GetProperty("event_type").GetString())
            .Should().Equal("book", "book");
    }

    [Fact]
    public void Decode_EmptyArrayFixture_ShouldReturnSuccessfulEmptyResult()
    {
        var result = _decoder.Decode(CreateMessage(ReadFixture("empty-array.json")));

        result.IsDecoded.Should().BeTrue();
        result.Issue.Should().BeNull();
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public void Decode_MixedArray_ShouldKeepAllOriginalIndexesAndItemIssue()
    {
        var result = _decoder.Decode(CreateMessage("""[{"id":0}, 1, {"id":2}]"""));

        var items = result.Items.ToArray();
        result.IsDecoded.Should().BeTrue();
        items.Select(item => item.RawItemIndex).Should().Equal(0, 1, 2);
        items[0].Json!.Value.GetProperty("id").GetInt32().Should().Be(0);
        items[1].IsDecoded.Should().BeFalse();
        items[1].Json.Should().BeNull();
        items[1].Issue.Should().BeEquivalentTo(new NormalizationIssue(
            "normalization.payload.item_kind.invalid",
            "Raw message array item must be a JSON object.",
            "$[1]"));
        items[2].Json!.Value.GetProperty("id").GetInt32().Should().Be(2);
    }

    [Theory]
    [InlineData("\"text\"")]
    [InlineData("123")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("null")]
    public void Decode_RootScalar_ShouldReturnRootKindIssue(string json)
    {
        var result = _decoder.Decode(CreateMessage(json));

        result.IsDecoded.Should().BeFalse();
        result.Items.Should().BeEmpty();
        result.Issue.Should().BeEquivalentTo(new NormalizationIssue(
            "normalization.payload.root_kind.invalid",
            "Raw message JSON root must be an object or an array.",
            "$"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("[{}")]
    [InlineData("{} trailing")]
    public void Decode_InvalidJson_ShouldReturnIssueInsteadOfParserException(string json)
    {
        var action = () => _decoder.Decode(CreateMessage(json));

        var result = action.Should().NotThrow().Subject;
        result.IsDecoded.Should().BeFalse();
        result.Items.Should().BeEmpty();
        result.Issue!.Code.Should().Be("normalization.payload.json.invalid");
    }

    [Fact]
    public void Decode_InvalidUtf8_ShouldReturnUtf8IssueInsteadOfParserException()
    {
        byte[] payload =
        [
            (byte)'{', (byte)'"', (byte)'v', (byte)'"', (byte)':', (byte)'"',
            0xC3, 0x28,
            (byte)'"', (byte)'}'
        ];

        var action = () => _decoder.Decode(CreateMessage(payload));

        var result = action.Should().NotThrow().Subject;
        result.IsDecoded.Should().BeFalse();
        result.Items.Should().BeEmpty();
        result.Issue!.Code.Should().Be("normalization.payload.utf8.invalid");
    }

    [Fact]
    public void Decode_NestedArrayItem_ShouldReturnIssueWithoutFlattening()
    {
        var result = _decoder.Decode(CreateMessage("""[[{"id":0}]]"""));

        var item = result.Items.Should().ContainSingle().Subject;
        item.RawItemIndex.Should().Be(0);
        item.IsDecoded.Should().BeFalse();
        item.Issue!.Field.Should().Be("$[0]");
    }

    private static RawMessageEnvelope CreateMessage(string json)
    {
        return CreateMessage(Encoding.UTF8.GetBytes(json));
    }

    private static RawMessageEnvelope CreateMessage(byte[] payload)
    {
        return new RawMessageEnvelope(
            42,
            CollectorSessionId.Create(
                Guid.Parse("6d9ac447-7bcc-4c85-8619-0384da429a33")).Value,
            DateTimeOffset.Parse("2026-08-10T10:00:00Z"),
            payload);
    }

    private static byte[] ReadFixture(string fileName)
    {
        var assembly = typeof(RawMessageDecoderTests).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith($".{fileName}", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
