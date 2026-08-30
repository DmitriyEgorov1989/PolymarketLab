using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StartCollector;
using PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StopCollector;
using PolymarketLab.DataCollection.Core.Application.UseCases.Common;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Presentation.Controllers;
using PolymarketLab.Framework.Response;
using PolymarketLab.Markets.Presentation.Controllers;
using Xunit;

namespace PolymarketLab.ApiContract.Tests;

public sealed class FrontendApiContractTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public void Controllers_ShouldExposeOnlyCanonicalWriteRoutes()
    {
        GetTemplate<HttpPostAttribute>(typeof(MarketController), "RegisterMarket")
            .Should().BeNull();
        GetTemplate<HttpPostAttribute>(typeof(CollectorController), "StartCollector")
            .Should().BeNull();
        GetTemplate<HttpPostAttribute>(typeof(CollectorController), "StopCollector")
            .Should().Be("{sessionId:guid}/stop");

        GetHttpTemplates(typeof(MarketController)).Should().NotContain("register");
        GetHttpTemplates(typeof(CollectorController)).Should().NotContain(["start", "stop"]);
    }

    [Fact]
    public void Controllers_ShouldExposeCanonicalReadRoutes()
    {
        GetTemplate<HttpGetAttribute>(typeof(MarketController), "GetMarkets")
            .Should().BeNull();
        GetTemplate<HttpGetAttribute>(typeof(MarketController), "GetMarketById")
            .Should().Be("{marketId:guid}");
        GetTemplate<HttpGetAttribute>(typeof(CollectorController), "GetCollectorSessionById")
            .Should().Be("{sessionId:guid}");
        GetTemplate<HttpGetAttribute>(typeof(CollectorController), "GetCollectorSessionByMarket")
            .Should().Be("by-market/{marketId:guid}");
    }

    [Fact]
    public void Envelope_ShouldUseCreatedUtc()
    {
        var json = Serialize(Envelope.Ok(new { value = 1 }));

        json["createdUtc"].Should().NotBeNull();
        json["createdOtc"].Should().BeNull();
        json["listErrors"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public void ErrorEnvelope_ShouldUseFrozenErrorShape()
    {
        var json = Serialize(Envelope.Errors(
        [
            new ResponseError(
                "collector.query.session.not_found",
                "Collector session was not found.",
                "sessionId")
        ]));

        json["result"].Should().BeNull();
        var error = json["listErrors"]!.AsArray().Single()!.AsObject();
        error.Select(property => property.Key).Should().BeEquivalentTo(
            "errorCode",
            "errorMessage",
            "invalidField");
        error["invalidField"]!.GetValue<string>().Should().Be("sessionId");
    }

    [Fact]
    public void CollectorStatuses_ShouldMatchFrozenStringValues()
    {
        Enum.GetNames<CollectorSessionStatus>().Should().Equal(
            "Starting",
            "Running",
            "Stopping",
            "Stopped",
            "Failed",
            "Interrupted",
            "Scheduled",
            "Invalidating");
    }

    [Fact]
    public void StartResponse_ShouldContainOnlyIdsAndStringStatus()
    {
        var response = new StartCollectorResponse(Guid.NewGuid(), Guid.NewGuid(), "Scheduled");

        var result = Serialize(response);

        result.Select(property => property.Key).Should().BeEquivalentTo(
            "sessionId",
            "marketId",
            "status");
        result["status"]!.GetValue<string>().Should().Be("Scheduled");
    }

    [Fact]
    public void StopResponse_ShouldContainFullSession()
    {
        var session = new CollectorSessionResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Failed",
            DateTimeOffset.Parse("2026-08-06T12:00:00Z"),
            DateTimeOffset.Parse("2026-08-06T12:00:01Z"),
            DateTimeOffset.Parse("2026-08-06T12:30:00Z"),
            "collector.runtime.receive.failed",
            "Receive failed.",
            120,
            118,
            DateTimeOffset.Parse("2026-08-06T12:29:59Z"),
            2);

        var result = Serialize(new StopCollectorResponse(session));

        result["session"]!["status"]!.GetValue<string>().Should().Be("Failed");
        result["session"]!["failureCode"]!.GetValue<string>()
            .Should().Be("collector.runtime.receive.failed");
        result["session"]!.AsObject().Select(property => property.Key)
            .Should().BeEquivalentTo(
                "sessionId",
                "marketId",
                "status",
                "createdAt",
                "startedAt",
                "stoppedAt",
                "failureCode",
                "failureMessage",
                "messagesReceived",
                "messagesPersisted",
                "lastMessageAt",
                "reconnectCount");
        result["session"]!["messagesReceived"]!.GetValue<long>().Should().Be(120);
        result["session"]!["messagesPersisted"]!.GetValue<long>().Should().Be(118);
        result["session"]!["reconnectCount"]!.GetValue<long>().Should().Be(2);
    }

    private static string? GetTemplate<TAttribute>(Type controllerType, string methodName)
        where TAttribute : HttpMethodAttribute
    {
        return controllerType.GetMethod(methodName)!
            .GetCustomAttribute<TAttribute>()!
            .Template;
    }

    private static IReadOnlyCollection<string?> GetHttpTemplates(Type controllerType)
    {
        return controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>())
            .Select(attribute => attribute.Template)
            .ToArray();
    }

    private static JsonObject Serialize<T>(T value)
    {
        return JsonSerializer.SerializeToNode(value, JsonOptions)!.AsObject();
    }
}
