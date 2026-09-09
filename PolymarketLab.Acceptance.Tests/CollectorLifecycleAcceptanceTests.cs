using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using PolymarketLab.Acceptance.Tests.Assertions;
using PolymarketLab.Acceptance.Tests.Host;
using PolymarketLab.Acceptance.Tests.PostgreSql;
using PolymarketLab.DataCollection.Core.Application.Normalization;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorScheduling;
using PolymarketLab.DataCollection.Core.Application.UseCases.ResolutionConsensus;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;
using PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime.WebSockets;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Resolution;
using Xunit;

namespace PolymarketLab.Acceptance.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class CollectorLifecycleAcceptanceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task FullLifecycle_ShouldReachStoppedWithExactDurableEvidence()
    {
        var eventStartsAt = DateTimeOffset.Parse("2026-09-05T12:00:00Z");
        var clock = new FakeTimeProvider(eventStartsAt.AddSeconds(-90));
        var scenario = new AcceptanceScenario(eventStartsAt);
        var socket = new ControllableWebSocketConnection();
        var socketFactory = new ControllableWebSocketFactory(socket);
        var normalizationGate = new NormalizationGate();
        await using var database = await fixture.CreateDatabaseAsync();
        await database.ApplyMigrationsAsync();
        await using var factory = new AcceptanceWebApplicationFactory(
            database.ConnectionString,
            clock,
            services => ConfigureScenario(
                services,
                scenario,
                socketFactory,
                normalizationGate));
        using var client = factory.CreateClient();

        var marketId = await RegisterMarketAsync(client);
        var sessionId = await StartCollectorAsync(client, marketId);
        var scheduled = await GetSessionAsync(client, sessionId);
        AssertState(scheduled, "Scheduled", "WaitingForPreparation");

        clock.Advance(TimeSpan.FromSeconds(30));
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            await socket.WaitForSubscriptionAsync(timeout.Token);
        await WaitForStateAsync(
            client,
            clock,
            sessionId,
            "Starting",
            "AwaitingInitialBooks",
            advanceClock: false);

        socket.Emit(BookMessage(AcceptanceScenario.YesTokenId));
        socket.Emit(BookMessage(AcceptanceScenario.NoTokenId));
        var ready = await WaitForStateAsync(
            client,
            clock,
            sessionId,
            "Running",
            "ReadyBeforeWindow");
        ready["subscriptionReadyAt"].Should().NotBeNull();
        socketFactory.CreateCount.Should().Be(1);
        socket.SentMessages.Should().Contain(message =>
            message.Contains(AcceptanceScenario.YesTokenId, StringComparison.Ordinal)
            && message.Contains(AcceptanceScenario.NoTokenId, StringComparison.Ordinal));

        AdvanceTo(clock, scenario.EventStartsAt);
        await TickResolutionAsync(factory.Services);
        await WaitForStateAsync(
            client,
            clock,
            sessionId,
            "Running",
            "CollectingWindow",
            advanceClock: false);

        AdvanceTo(clock, scenario.EventEndsAt);
        await TickResolutionAsync(factory.Services);
        await WaitForStateAsync(
            client,
            clock,
            sessionId,
            "Running",
            "AwaitingResolution",
            advanceClock: false);
        scenario.IsResolved = true;
        socket.Emit(ResolutionMessage());
        await WaitForRawCountAsync(client, sessionId, minimumCount: 3);
        clock.Advance(TimeSpan.FromSeconds(2));
        await TickResolutionAsync(factory.Services);
        await WaitForStateAsync(
            client,
            clock,
            sessionId,
            "Stopping",
            "AwaitingNormalization",
            advanceClock: false);

        normalizationGate.Release();
        await WaitForResolutionNormalizationAsync(client, sessionId);
        await TickResolutionAsync(factory.Services);
        var stopped = await WaitForStateAsync(
            client,
            clock,
            sessionId,
            "Stopped",
            null,
            advanceClock: false);
        stopped["stopReason"]!.GetValue<string>().Should().Be("MarketClosed");
        stopped["cleanup"].Should().BeNull();
        stopped["resolution"]!["winningTokenId"]!.GetValue<string>()
            .Should().Be(AcceptanceScenario.YesTokenId);
        stopped["resolution"]!["winningOutcome"]!.GetValue<string>()
            .Should().Be("Yes");
        stopped["normalization"]!["resolutionRawItemProcessed"]!.GetValue<bool>()
            .Should().BeTrue();

        var evidence = await DurableCollectorAssertions.ReadAsync(
            database.ConnectionString,
            sessionId,
            projectionVersion: 1);
        evidence.MessagesReceived.Should().BeGreaterThan(0);
        evidence.MessagesEnqueued.Should().Be(evidence.MessagesReceived);
        evidence.MessagesPersisted.Should().Be(evidence.MessagesReceived);
        evidence.RawCount.Should().Be(evidence.MessagesReceived);
        evidence.ProcessedCount.Should().Be(evidence.MessagesReceived);
    }

    [Fact]
    public async Task Restart_ShouldInvalidateActiveSessionAndCleanupDurableDataset()
    {
        var eventStartsAt = DateTimeOffset.Parse("2026-09-05T12:00:00Z");
        var clock = new FakeTimeProvider(eventStartsAt.AddSeconds(-90));
        var scenario = new AcceptanceScenario(eventStartsAt);
        await using var database = await fixture.CreateDatabaseAsync();
        await database.ApplyMigrationsAsync();

        Guid sessionId;
        DurableCollectorEvidence evidenceBeforeRestart;
        await using (var factory = new AcceptanceWebApplicationFactory(
            database.ConnectionString,
            clock,
            services => ConfigureScenario(
                services,
                scenario,
                new ControllableWebSocketFactory(
                    new ControllableWebSocketConnection()),
                new NormalizationGate())))
        {
            using var client = factory.CreateClient();
            var marketId = await RegisterMarketAsync(client);
            sessionId = await StartCollectorAsync(client, marketId);

            clock.Advance(TimeSpan.FromSeconds(30));
            var socketFactory = factory.Services
                .GetRequiredService<ICollectorWebSocketFactory>()
                .Should().BeOfType<ControllableWebSocketFactory>().Subject;
            var socket = socketFactory.Connection;
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                await socket.WaitForSubscriptionAsync(timeout.Token);
            socket.Emit(BookMessage(AcceptanceScenario.YesTokenId));
            socket.Emit(BookMessage(AcceptanceScenario.NoTokenId));
            await WaitForRawCountAsync(client, sessionId, minimumCount: 2);

            evidenceBeforeRestart = await DurableCollectorAssertions.ReadAsync(
                database.ConnectionString,
                sessionId,
                projectionVersion: 1);
            evidenceBeforeRestart.RawCount.Should().BeGreaterThan(0);
        }

        await using var restartedFactory = new AcceptanceWebApplicationFactory(
            database.ConnectionString,
            clock,
            services => ConfigureScenario(
                services,
                scenario,
                new ControllableWebSocketFactory(
                    new ControllableWebSocketConnection()),
                new NormalizationGate()));
        using var restartedClient = restartedFactory.CreateClient();

        var failed = await GetSessionAsync(restartedClient, sessionId);
        AssertState(failed, "Failed", null);
        failed["stopReason"]!.GetValue<string>().Should().Be("ProcessTerminated");
        failed["failureCode"]!.GetValue<string>()
            .Should().Be("collector.session.process_terminated");
        failed["cleanup"].Should().NotBeNull();
        failed["cleanup"]!["deletedRawMessageCount"]!.GetValue<long>()
            .Should().Be(evidenceBeforeRestart.RawCount);

        var evidenceAfterRestart = await DurableCollectorAssertions.ReadAsync(
            database.ConnectionString,
            sessionId,
            projectionVersion: 1);
        evidenceAfterRestart.RawCount.Should().Be(0);
        evidenceAfterRestart.ProcessedCount.Should().Be(0);
    }

    [Fact]
    public async Task ManualStop_ThenSchedulerTick_ShouldCleanupWithoutRestartAndRejectLateWrites()
    {
        var eventStartsAt = DateTimeOffset.Parse("2026-09-05T12:00:00Z");
        var clock = new FakeTimeProvider(eventStartsAt.AddSeconds(-90));
        var scenario = new AcceptanceScenario(eventStartsAt);
        var socket = new ControllableWebSocketConnection();
        await using var database = await fixture.CreateDatabaseAsync();
        await database.ApplyMigrationsAsync();
        await using var factory = new AcceptanceWebApplicationFactory(
            database.ConnectionString,
            clock,
            services =>
            {
                ConfigureScenario(
                    services, scenario, new ControllableWebSocketFactory(socket), new NormalizationGate());
                services.Remove(services.Single(descriptor =>
                    descriptor.ServiceType == typeof(IHostedService)
                    && descriptor.ImplementationType == typeof(CollectorSchedulerBackgroundService)));
            });
        using var client = factory.CreateClient();
        var marketId = await RegisterMarketAsync(client);
        var sessionId = await StartCollectorAsync(client, marketId);

        clock.Advance(TimeSpan.FromSeconds(30));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tick = await scope.ServiceProvider.GetRequiredService<ICollectorScheduler>()
                .TickAsync(CancellationToken.None);
            tick.IsSuccess.Should().BeTrue();
        }
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            await socket.WaitForSubscriptionAsync(timeout.Token);
        socket.Emit(BookMessage(AcceptanceScenario.YesTokenId));
        socket.Emit(BookMessage(AcceptanceScenario.NoTokenId));
        await WaitForStateAsync(client, clock, sessionId, "Running", "ReadyBeforeWindow", advanceClock: false);
        await WaitForRawCountAsync(client, sessionId, minimumCount: 2);

        ClaimedRawMessage claim;
        NormalizationCompletion completion;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            claim = (await scope.ServiceProvider.GetRequiredService<IRawMessageNormalizationClaimRepository>()
                .ClaimBatchAsync(1, 1, TimeSpan.FromMinutes(5), CancellationToken.None)).Single();
            completion = NormalizationCompletion.Processed(
            [
                new NormalizedEvent(
                    claim.Message.RawMessageId, 0, claim.ProjectionVersion, 1, "book",
                    claim.Message.SessionId, claim.Message.ReceivedAt, null,
                    AcceptanceScenario.ConditionId, AcceptanceScenario.YesTokenId,
                    [new BookSnapshotRecord("test-book", null, null)])
            ]);
            (await scope.ServiceProvider.GetRequiredService<INormalizedMessageWriter>()
                .WriteAsync(claim, completion, CancellationToken.None))
                .Should().Be(NormalizationWriteStatus.Written);
        }

        using var stopResponse = await client.PostAsync($"/api/Collector/{sessionId}/stop", null);
        stopResponse.EnsureSuccessStatusCode();
        var invalidating = await GetSessionAsync(client, sessionId);
        AssertState(invalidating, "Invalidating", "Cleaning");
        invalidating["cleanup"].Should().BeNull();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tick = await scope.ServiceProvider.GetRequiredService<ICollectorScheduler>()
                .TickAsync(CancellationToken.None);
            tick.IsSuccess.Should().BeTrue();
        }

        var failed = await GetSessionAsync(client, sessionId);
        AssertState(failed, "Failed", null);
        failed["stopReason"]!.GetValue<string>().Should().Be("Requested");
        failed["failureCode"]!.GetValue<string>().Should().Be("collector.stop.requested");
        failed["cleanup"]!["deletedRawMessageCount"]!.GetValue<long>().Should().Be(2);
        failed["cleanup"]!["deletedNormalizationCount"]!.GetValue<long>().Should().Be(1);
        failed["cleanup"]!["deletedNormalizedEventCount"]!.GetValue<long>().Should().Be(1);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<ICollectorSessionRepository>()
                .GetActiveAsync(CancellationToken.None)).Should().BeEmpty();
            var lateRaw = await scope.ServiceProvider.GetRequiredService<IRawMarketMessageWriter>()
                .WriteBatchAsync(
                    [new RawMarketMessage(claim.Message.SessionId, 1, clock.GetUtcNow(),
                        Encoding.UTF8.GetBytes(BookMessage(AcceptanceScenario.YesTokenId)))],
                    [], CancellationToken.None);
            lateRaw.FencedSessionIds.Should().Equal(claim.Message.SessionId);
            (await scope.ServiceProvider.GetRequiredService<INormalizedMessageWriter>()
                .WriteAsync(claim, completion, CancellationToken.None))
                .Should().Be(NormalizationWriteStatus.ClaimLost);
        }

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<DataCollectionDbContext>();
        (await context.CollectorDatasetCleanupAudits.CountAsync()).Should().Be(1);
        (await context.RawMarketMessages.CountAsync()).Should().Be(0);
        (await context.RawMessageNormalizations.CountAsync()).Should().Be(0);
        (await context.NormalizedEvents.CountAsync()).Should().Be(0);
        (await context.BookSnapshots.CountAsync()).Should().Be(0);
    }

    private static void ConfigureScenario(
        IServiceCollection services,
        AcceptanceScenario scenario,
        ControllableWebSocketFactory socketFactory,
        NormalizationGate normalizationGate)
    {
        var resolutionBackgroundService = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(ResolutionConsensusBackgroundService));
        services.Remove(resolutionBackgroundService);
        services.RemoveAll<ICollectorWebSocketFactory>();
        services.AddSingleton<ICollectorWebSocketFactory>(socketFactory);
        services.AddSingleton<IHttpMessageHandlerBuilderFilter>(
            new ScenarioHttpMessageHandlerBuilderFilter(scenario));
        services.AddSingleton(normalizationGate);
        services.RemoveAll<INormalizationProcessor>();
        services.AddScoped<INormalizationProcessor>(serviceProvider =>
            new GatedNormalizationProcessor(
                serviceProvider.GetRequiredService<NormalizationProcessor>(),
                normalizationGate));
    }

    private static async Task TickResolutionAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var coordinator = scope.ServiceProvider
            .GetRequiredService<IResolutionConsensusCoordinator>();
        var result = await coordinator.TickAsync(CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    private static async Task<Guid> RegisterMarketAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/Market",
            new { marketUri = $"https://polymarket.com/event/{AcceptanceScenario.EventSlug}" });
        response.EnsureSuccessStatusCode();
        var envelope = await response.ReadEnvelopeAsync();
        return envelope["result"]!["marketId"]!.GetValue<Guid>();
    }

    private static async Task<Guid> StartCollectorAsync(HttpClient client, Guid marketId)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/Collector",
            new { marketId });
        response.EnsureSuccessStatusCode();
        var envelope = await response.ReadEnvelopeAsync();
        return envelope["result"]!["sessionId"]!.GetValue<Guid>();
    }

    private static async Task<JsonObject> GetSessionAsync(HttpClient client, Guid sessionId)
    {
        using var response = await client.GetAsync($"/api/Collector/{sessionId}");
        response.EnsureSuccessStatusCode();
        var envelope = await response.ReadEnvelopeAsync();
        return envelope["result"]!["session"]!.AsObject();
    }

    private static async Task<JsonObject> WaitForStateAsync(
        HttpClient client,
        FakeTimeProvider clock,
        Guid sessionId,
        string status,
        string? phase,
        bool advanceClock = true)
    {
        JsonObject? latest = null;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            latest = await GetSessionAsync(client, sessionId);
            if (latest["status"]!.GetValue<string>() == status
                && latest["phase"]?.GetValue<string>() == phase)
            {
                return latest;
            }

            if (advanceClock)
                clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(20);
        }

        throw new Xunit.Sdk.XunitException(
            $"Session did not reach {status}/{phase}. Last state was "
            + $"{latest?["status"]}/{latest?["phase"]}.");
    }

    private static async Task WaitForRawCountAsync(
        HttpClient client,
        Guid sessionId,
        long minimumCount)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var session = await GetSessionAsync(client, sessionId);
            if (session["remainingRawMessageCount"]!.GetValue<long>() >= minimumCount)
                return;

            await Task.Delay(20);
        }

        throw new Xunit.Sdk.XunitException(
            $"Session did not persist {minimumCount} raw messages.");
    }

    private static async Task WaitForResolutionNormalizationAsync(
        HttpClient client,
        Guid sessionId)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var session = await GetSessionAsync(client, sessionId);
            if (session["normalization"]?["resolutionRawItemProcessed"]
                    ?.GetValue<bool>() == true)
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new Xunit.Sdk.XunitException(
            "Resolution raw item was not normalized.");
    }

    private static void AssertState(JsonObject session, string status, string? phase)
    {
        session["status"]!.GetValue<string>().Should().Be(status);
        session["phase"]?.GetValue<string>().Should().Be(phase);
    }

    private static void AdvanceTo(FakeTimeProvider clock, DateTimeOffset target)
    {
        var delta = target - clock.GetUtcNow();
        if (delta > TimeSpan.Zero)
            clock.Advance(delta);
    }

    private static string BookMessage(string tokenId) => $$"""
        {
          "event_type":"book",
          "market":"{{AcceptanceScenario.ConditionId}}",
          "asset_id":"{{tokenId}}",
          "hash":"hash-{{tokenId}}",
          "timestamp":"1788600000000",
          "bids":[],
          "asks":[]
        }
        """;

    private static string ResolutionMessage() => $$"""
        {
          "event_type":"market_resolved",
          "id":"{{AcceptanceScenario.MarketId}}",
          "market":"{{AcceptanceScenario.ConditionId}}",
          "assets_ids":["{{AcceptanceScenario.YesTokenId}}","{{AcceptanceScenario.NoTokenId}}"],
          "winning_asset_id":"{{AcceptanceScenario.YesTokenId}}",
          "winning_outcome":"Yes",
          "timestamp":"1788600300000"
        }
        """;
}
