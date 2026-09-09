using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using PolymarketLab.Acceptance.Tests.Assertions;
using PolymarketLab.Acceptance.Tests.Host;
using PolymarketLab.Acceptance.Tests.PostgreSql;
using Xunit;

namespace PolymarketLab.Acceptance.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class CollectorHttpPipelineAcceptanceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task MissingMarketRequestBody_ShouldReturnModelBindingEnvelope()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await database.ApplyMigrationsAsync();
        await using var factory = CreateFactory(database);
        using var client = factory.CreateClient();

        using var content = new StringContent(string.Empty, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/Market", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var envelope = await response.ReadEnvelopeAsync();
        envelope["result"].Should().BeNull();
        envelope.ErrorCodes().Should().Contain("request.body.required");
    }

    [Fact]
    public async Task MalformedJson_ShouldReturnValidationEnvelope()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await database.ApplyMigrationsAsync();
        await using var factory = CreateFactory(database);
        using var client = factory.CreateClient();
        using var content = new StringContent("{", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/Collector", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var envelope = await response.ReadEnvelopeAsync();
        envelope["result"].Should().BeNull();
        envelope.ErrorCodes().Should().Contain("request.validation");
    }

    [Fact]
    public async Task UnknownRoute_ShouldReturnStatusCodeEnvelope()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await database.ApplyMigrationsAsync();
        await using var factory = CreateFactory(database);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/not-existing");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var envelope = await response.ReadEnvelopeAsync();
        envelope["result"].Should().BeNull();
        envelope.ErrorCodes().Should().ContainSingle().Which.Should().Be("http.not_found");
    }

    private static AcceptanceWebApplicationFactory CreateFactory(AcceptanceDatabase database) =>
        new(
            database.ConnectionString,
            new FakeTimeProvider(DateTimeOffset.Parse("2026-09-05T12:00:00Z")));
}
