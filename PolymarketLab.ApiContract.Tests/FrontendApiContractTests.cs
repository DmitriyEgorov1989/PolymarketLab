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
using PolymarketLab.DataCollection.Core.Ports.Dtos;
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
    public void CollectorPhases_ShouldMatchFrozenStringValues()
    {
        Enum.GetNames<CollectorSessionPhase>().Should().Equal(
            "WaitingForPreparation",
            "Connecting",
            "AwaitingInitialBooks",
            "AwaitingHeartbeat",
            "ReadyBeforeWindow",
            "CollectingWindow",
            "AwaitingResolution",
            "DrainingRaw",
            "AwaitingNormalization",
            "Cleaning");
    }

    [Fact]
    public void ResolutionObservationSources_ShouldMatchFrozenStringValues()
    {
        Enum.GetNames<ResolutionObservationSource>().Should().Equal(
            "WebSocket", "Gamma", "Clob");
    }

    [Fact]
    public void DurableResolutionObservationStatuses_ShouldMatchFrozenStringValues()
    {
        Enum.GetNames<DurableResolutionObservationStatus>().Should().Equal(
            "Rejected", "NonTerminal", "Terminal", "Failed", "Conflict");
    }

    [Fact]
    public void CollectorStopReasons_ShouldMatchFrozenStringValues()
    {
        Enum.GetNames<CollectorStopReason>().Should().Equal(
            "Requested",
            "ApplicationShutdown",
            "MarketClosed",
            "FatalWebSocketError",
            "PersistenceFailure",
            "RecoveryTimeout",
            "StartupFailure",
            "ProcessTerminated",
            "ResolutionFailure");
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
        var result = Serialize(new StopCollectorResponse(CreateCompleteResponse()));

        var session = result["session"]!.AsObject();
        session.Select(property => property.Key).Should().BeEquivalentTo(
            "sessionId",
            "marketId",
            "snapshot",
            "status",
            "phase",
            "effectiveDeadline",
            "createdAt",
            "startedAt",
            "subscriptionReadyAt",
            "stoppedAt",
            "invalidatingAt",
            "stopReason",
            "failureCode",
            "failureMessage",
            "readiness",
            "messagesReceived",
            "messagesEnqueued",
            "messagesPersisted",
            "remainingRawMessageCount",
            "lastMessageAt",
            "reconnectCount",
            "normalization",
            "resolution",
            "cleanup");
        session["status"]!.GetValue<string>().Should().Be("Stopping");
        session["phase"]!.GetValue<string>().Should().Be("AwaitingNormalization");
        session["messagesReceived"]!.GetValue<long>().Should().Be(1250);
        session["messagesPersisted"]!.GetValue<long>().Should().Be(1250);
        session["remainingRawMessageCount"]!.GetValue<long>().Should().Be(1250);
    }

    [Fact]
    public void CollectorSessionResponse_ShouldExposeExactSnapshotShape()
    {
        var session = Serialize(CreateCompleteResponse());

        var snapshot = session["snapshot"]!.AsObject();
        snapshot.Select(property => property.Key).Should().BeEquivalentTo(
            "externalEventId",
            "eventSlug",
            "externalMarketId",
            "marketSlug",
            "conditionId",
            "eventStartsAt",
            "eventEndsAt",
            "projectionVersion",
            "tokens");
        snapshot["projectionVersion"]!.GetValue<int>().Should().Be(3);
        snapshot["tokens"]!.AsArray().Should().HaveCount(2);

        var token = snapshot["tokens"]![0]!.AsObject();
        token.Select(property => property.Key).Should().BeEquivalentTo(
            "tokenId",
            "outcome",
            "outcomeIndex");
        token["tokenId"]!.GetValue<string>().Should().Be("1001");
        token["outcome"]!.GetValue<string>().Should().Be("Yes");
        token["outcomeIndex"]!.GetValue<int>().Should().Be(0);
    }

    [Fact]
    public void CollectorSessionResponse_ShouldExposeExactReadinessShape()
    {
        var session = Serialize(CreateCompleteResponse());

        var readiness = session["readiness"]!.AsObject();
        readiness.Select(property => property.Key).Should().BeEquivalentTo(
            "connectionEpoch",
            "tokens");
        readiness["connectionEpoch"]!.GetValue<long>().Should().Be(2);

        var token = readiness["tokens"]!.AsArray()[0]!.AsObject();
        token.Select(property => property.Key).Should().BeEquivalentTo(
            "tokenId",
            "initialBookEnqueuedAt");
        token["initialBookEnqueuedAt"]!.GetValue<DateTimeOffset>()
            .Should().Be(DateTimeOffset.Parse("2026-09-04T11:59:44Z"));
    }

    [Fact]
    public void CollectorSessionResponse_ShouldExposeExactNormalizationShape()
    {
        var session = Serialize(CreateCompleteResponse());

        var normalization = session["normalization"]!.AsObject();
        normalization.Select(property => property.Key).Should().BeEquivalentTo(
            "rawCount",
            "ledgerCount",
            "processedCount",
            "pendingCount",
            "processingCount",
            "unsupportedCount",
            "invalidCount",
            "failedCount",
            "missingCount",
            "resolutionRawItemProcessed");
        normalization["rawCount"]!.GetValue<long>().Should().Be(1250);
        normalization["pendingCount"]!.GetValue<long>().Should().Be(10);
    }

    [Fact]
    public void CollectorSessionResponse_ShouldExposeExactResolutionShape()
    {
        var session = Serialize(CreateCompleteResponse());

        var resolution = session["resolution"]!.AsObject();
        resolution.Select(property => property.Key).Should().BeEquivalentTo(
            "signaledAt",
            "confirmedAt",
            "winningTokenId",
            "winningOutcome",
            "connectionEpoch",
            "lastPollingCycleAt",
            "sourceStates",
            "confirmationSources");

        var source = resolution["sourceStates"]!.AsArray().Single()!.AsObject();
        source.Select(property => property.Key).Should().BeEquivalentTo(
            "source",
            "status",
            "observedAt",
            "winningTokenId",
            "winningOutcome",
            "errorCode",
            "errorMessage");
        source["source"]!.GetValue<string>().Should().Be("WebSocket");
        source["status"]!.GetValue<string>().Should().Be("Terminal");
        source["winningTokenId"]!.GetValue<string>().Should().Be("1001");
        source["winningOutcome"]!.GetValue<string>().Should().Be("Yes");
    }

    [Fact]
    public void CollectorSessionResponse_WithCleanup_ShouldExposeExactCleanupShape()
    {
        var response = CreateCompleteResponse() with
        {
            Cleanup = new CollectorCleanupResponse(
                DateTimeOffset.Parse("2026-09-04T12:06:00Z"),
                DateTimeOffset.Parse("2026-09-04T12:07:00Z"),
                3,
                "collector.runtime.receive.failed",
                "Receive failed.",
                1250,
                1250,
                3),
            Normalization = null
        };

        var cleanup = Serialize(response)["cleanup"]!.AsObject();
        cleanup.Select(property => property.Key).Should().BeEquivalentTo(
            "invalidatingAt",
            "cleanedAt",
            "projectionVersion",
            "failureCode",
            "failureMessage",
            "deletedRawMessageCount",
            "deletedNormalizationCount",
            "deletedNormalizedEventCount");
        cleanup["cleanedAt"]!.GetValue<DateTimeOffset>()
            .Should().Be(DateTimeOffset.Parse("2026-09-04T12:07:00Z"));
        cleanup["deletedRawMessageCount"]!.GetValue<long>().Should().Be(1250);
    }

    [Fact]
    public void CollectorSessionResponse_LegacyInterrupted_ShouldExposeNullableShape()
    {
        var response = new CollectorSessionResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new CollectorSessionSnapshotResponse(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                []),
            "Interrupted",
            null,
            null,
            DateTimeOffset.Parse("2026-08-06T12:00:00Z"),
            null,
            null,
            DateTimeOffset.Parse("2026-08-06T12:30:00Z"),
            null,
            "ProcessTerminated",
            null,
            null,
            new CollectorReadinessResponse(0, []),
            0,
            0,
            0,
            0,
            null,
            0,
            null,
            new CollectorResolutionResponse(null, null, null, null, null, null, [], []),
            null);

        var session = Serialize(response);
        session["snapshot"]!.AsObject()["projectionVersion"].Should().BeNull();
        session["snapshot"]!.AsObject()["tokens"]!.AsArray().Should().BeEmpty();
        session["phase"].Should().BeNull();
        session["effectiveDeadline"].Should().BeNull();
        session["normalization"].Should().BeNull();
        session["resolution"]!.AsObject()["sourceStates"]!.AsArray().Should().BeEmpty();
        session["resolution"]!.AsObject()["confirmationSources"]!.AsArray().Should().BeEmpty();
        session["cleanup"].Should().BeNull();
        session["stopReason"]!.GetValue<string>().Should().Be("ProcessTerminated");
    }

    [Fact]
    public void CollectorSessionResponse_ShouldNotExposeUnsafeProvenanceFields()
    {
        var json = Serialize(CreateCompleteResponse()).ToJsonString();

        json.Should().NotContain("rawPayload");
        json.Should().NotContain("credentials");
        json.Should().NotContain("stackTrace");
        json.Should().NotContain("rawMessageId");
        json.Should().NotContain("rawItemIndex");
        json.Should().NotContain("outcomes");
    }

    private static CollectorSessionResponse CreateCompleteResponse() => new(
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        new CollectorSessionSnapshotResponse(
            "event-123",
            "btc-updown-5m-1200",
            "market-123",
            "btc-updown-5m-1200",
            "0xabc",
            DateTimeOffset.Parse("2026-09-04T12:00:00Z"),
            DateTimeOffset.Parse("2026-09-04T12:05:00Z"),
            3,
            [
                new CollectorSessionTokenResponse("1001", "Yes", 0),
                new CollectorSessionTokenResponse("1002", "No", 1)
            ]),
        "Stopping",
        "AwaitingNormalization",
        DateTimeOffset.Parse("2026-09-04T12:10:04Z"),
        DateTimeOffset.Parse("2026-09-04T11:57:00Z"),
        DateTimeOffset.Parse("2026-09-04T11:59:00Z"),
        DateTimeOffset.Parse("2026-09-04T11:59:48Z"),
        null,
        null,
        null,
        null,
        null,
        new CollectorReadinessResponse(
            2,
            [
                new CollectorTokenReadinessResponse(
                    "1001",
                    DateTimeOffset.Parse("2026-09-04T11:59:44Z")),
                new CollectorTokenReadinessResponse(
                    "1002",
                    DateTimeOffset.Parse("2026-09-04T11:59:45Z"))
            ]),
        1250,
        1250,
        1250,
        1250,
        DateTimeOffset.Parse("2026-09-04T12:05:03Z"),
        1,
        new CollectorNormalizationResponse(
            1250,
            1250,
            1240,
            10,
            0,
            0,
            0,
            0,
            0,
            false),
        new CollectorResolutionResponse(
            DateTimeOffset.Parse("2026-09-04T12:05:01Z"),
            DateTimeOffset.Parse("2026-09-04T12:05:03Z"),
            "1001",
            "Yes",
            2,
            DateTimeOffset.Parse("2026-09-04T12:05:02Z"),
            [
                new CollectorResolutionSourceResponse(
                    "WebSocket",
                    "Terminal",
                    DateTimeOffset.Parse("2026-09-04T12:05:01Z"),
                    "1001",
                    "Yes",
                    null,
                    null)
            ],
            [
                new CollectorResolutionSourceResponse(
                    "WebSocket",
                    "Terminal",
                    DateTimeOffset.Parse("2026-09-04T12:05:01Z"),
                    "1001",
                    "Yes",
                    null,
                    null)
            ]),
        null);

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
