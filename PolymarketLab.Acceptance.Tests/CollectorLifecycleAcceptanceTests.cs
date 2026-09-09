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

        var marketId = await RegisterMarketAsync(client, scenario);
        var sessionId = await GetSessionIdByMarketAsync(client, marketId);
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

        socket.Emit(BookMessage(scenario, scenario.YesTokenId));
        socket.Emit(BookMessage(scenario, scenario.NoTokenId));
        var ready = await WaitForStateAsync(
            client,
            clock,
            sessionId,
            "Running",
            "ReadyBeforeWindow");
        ready["subscriptionReadyAt"].Should().NotBeNull();
        socketFactory.CreateCount.Should().Be(1);
        socket.SentMessages.Should().Contain(message =>
            message.Contains(scenario.YesTokenId, StringComparison.Ordinal)
            && message.Contains(scenario.NoTokenId, StringComparison.Ordinal));

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
        socket.Emit(ResolutionMessage(scenario));
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
            .Should().Be(scenario.YesTokenId);
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
        AssertConsensusEvidence(evidence);

        var repeatedMarketId = await RegisterMarketAsync(client, scenario);
        repeatedMarketId.Should().Be(marketId);
        (await GetSessionIdByMarketAsync(client, marketId)).Should().Be(sessionId);
    }

    [Fact]
    public async Task OverlappingMarkets_ShouldCompleteIndependentlyWithExactDurableEvidence()
    {
        var eventStartsAt = DateTimeOffset.Parse("2026-09-05T12:00:00Z");
        var clock = new FakeTimeProvider(eventStartsAt.AddSeconds(-90));
        var firstScenario = new AcceptanceScenario(eventStartsAt, "first");
        var secondScenario = new AcceptanceScenario(eventStartsAt, "second");
        var firstSocket = new ControllableWebSocketConnection();
        var secondSocket = new ControllableWebSocketConnection();
        var socketFactory = new ControllableWebSocketFactory(firstSocket, secondSocket);
        var normalizationGate = new NormalizationGate();
        await using var database = await fixture.CreateDatabaseAsync();
        await database.ApplyMigrationsAsync();
        await using var factory = new AcceptanceWebApplicationFactory(
            database.ConnectionString,
            clock,
            services => ConfigureScenarios(
                services,
                [firstScenario, secondScenario],
                socketFactory,
                normalizationGate));
        using var client = factory.CreateClient();

        var firstMarketId = await RegisterMarketAsync(client, firstScenario);
        var secondMarketId = await RegisterMarketAsync(client, secondScenario);
        var firstSessionId = await GetSessionIdByMarketAsync(client, firstMarketId);
        var secondSessionId = await GetSessionIdByMarketAsync(client, secondMarketId);
        firstSessionId.Should().NotBe(secondSessionId);
        AssertState(await GetSessionAsync(client, firstSessionId), "Scheduled", "WaitingForPreparation");
        AssertState(await GetSessionAsync(client, secondSessionId), "Scheduled", "WaitingForPreparation");

        clock.Advance(TimeSpan.FromSeconds(30));
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            await firstSocket.WaitForSubscriptionAsync(timeout.Token);
            await secondSocket.WaitForSubscriptionAsync(timeout.Token);
        }

        var firstMarketSocket = SocketFor(socketFactory, firstScenario);
        var secondMarketSocket = SocketFor(socketFactory, secondScenario);
        firstMarketSocket.Should().NotBeSameAs(secondMarketSocket);
        firstMarketSocket.Emit(BookMessage(firstScenario, firstScenario.YesTokenId));
        firstMarketSocket.Emit(BookMessage(firstScenario, firstScenario.NoTokenId));
        secondMarketSocket.Emit(BookMessage(secondScenario, secondScenario.YesTokenId));
        secondMarketSocket.Emit(BookMessage(secondScenario, secondScenario.NoTokenId));
        await WaitForStateAsync(client, clock, firstSessionId, "Running", "ReadyBeforeWindow");
        await WaitForStateAsync(client, clock, secondSessionId, "Running", "ReadyBeforeWindow");

        AdvanceTo(clock, eventStartsAt);
        await TickResolutionAsync(factory.Services);
        await WaitForStateAsync(client, clock, firstSessionId, "Running", "CollectingWindow", false);
        await WaitForStateAsync(client, clock, secondSessionId, "Running", "CollectingWindow", false);

        AdvanceTo(clock, firstScenario.EventEndsAt);
        await TickResolutionAsync(factory.Services);
        await WaitForStateAsync(client, clock, firstSessionId, "Running", "AwaitingResolution", false);
        await WaitForStateAsync(client, clock, secondSessionId, "Running", "AwaitingResolution", false);
        firstScenario.IsResolved = true;
        secondScenario.IsResolved = true;
        firstMarketSocket.Emit(ResolutionMessage(firstScenario));
        secondMarketSocket.Emit(ResolutionMessage(secondScenario));
        await WaitForRawCountAsync(client, firstSessionId, minimumCount: 3);
        await WaitForRawCountAsync(client, secondSessionId, minimumCount: 3);
        clock.Advance(TimeSpan.FromSeconds(2));
        await TickResolutionAsync(factory.Services);
        await WaitForStateAsync(client, clock, firstSessionId, "Stopping", "AwaitingNormalization", false);
        await WaitForStateAsync(client, clock, secondSessionId, "Stopping", "AwaitingNormalization", false);

        normalizationGate.Release();
        await WaitForResolutionNormalizationAsync(client, firstSessionId);
        await WaitForResolutionNormalizationAsync(client, secondSessionId);
        await TickResolutionAsync(factory.Services);
        var firstStopped = await WaitForStateAsync(client, clock, firstSessionId, "Stopped", null, false);
        var secondStopped = await WaitForStateAsync(client, clock, secondSessionId, "Stopped", null, false);
        firstStopped["stopReason"]!.GetValue<string>().Should().Be("MarketClosed");
        secondStopped["stopReason"]!.GetValue<string>().Should().Be("MarketClosed");

        foreach (var sessionId in new[] { firstSessionId, secondSessionId })
        {
            var evidence = await DurableCollectorAssertions.ReadAsync(
                database.ConnectionString,
                sessionId,
                projectionVersion: 1);
            evidence.MessagesReceived.Should().BeGreaterThan(0);
            evidence.MessagesEnqueued.Should().Be(evidence.MessagesReceived);
            evidence.MessagesPersisted.Should().Be(evidence.MessagesReceived);
            evidence.RawCount.Should().Be(evidence.MessagesReceived);
            evidence.ProcessedCount.Should().Be(evidence.MessagesReceived);
            AssertConsensusEvidence(evidence);
        }
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
            var marketId = await RegisterMarketAsync(client, scenario);
            sessionId = await GetSessionIdByMarketAsync(client, marketId);

            clock.Advance(TimeSpan.FromSeconds(30));
            var socketFactory = factory.Services
                .GetRequiredService<ICollectorWebSocketFactory>()
                .Should().BeOfType<ControllableWebSocketFactory>().Subject;
            var socket = socketFactory.Connection;
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                await socket.WaitForSubscriptionAsync(timeout.Token);
            socket.Emit(BookMessage(scenario, scenario.YesTokenId));
            socket.Emit(BookMessage(scenario, scenario.NoTokenId));
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
    public async Task RestartBeforePreparation_ShouldKeepScheduledJobAndResumePreparation()
    {
        var eventStartsAt = DateTimeOffset.Parse("2026-09-05T12:00:00Z");
        var clock = new FakeTimeProvider(eventStartsAt.AddSeconds(-90));
        var scenario = new AcceptanceScenario(eventStartsAt);
        await using var database = await fixture.CreateDatabaseAsync();
        await database.ApplyMigrationsAsync();

        Guid marketId;
        Guid sessionId;
        await using (var factory = new AcceptanceWebApplicationFactory(
            database.ConnectionString,
            clock,
            services => ConfigureScenario(
                services,
                scenario,
                new ControllableWebSocketFactory(new ControllableWebSocketConnection()),
                new NormalizationGate())))
        {
            using var client = factory.CreateClient();
            marketId = await RegisterMarketAsync(client, scenario);
            sessionId = await GetSessionIdByMarketAsync(client, marketId);
            AssertState(await GetSessionAsync(client, sessionId), "Scheduled", "WaitingForPreparation");
        }

        var restartedSocket = new ControllableWebSocketConnection();
        await using var restartedFactory = new AcceptanceWebApplicationFactory(
            database.ConnectionString,
            clock,
            services => ConfigureScenario(
                services,
                scenario,
                new ControllableWebSocketFactory(restartedSocket),
                new NormalizationGate()));
        using var restartedClient = restartedFactory.CreateClient();
        var restoredSessionId = await GetSessionIdByMarketAsync(restartedClient, marketId);
        restoredSessionId.Should().Be(sessionId);
        AssertState(
            await GetSessionAsync(restartedClient, sessionId),
            "Scheduled",
            "WaitingForPreparation");

        clock.Advance(TimeSpan.FromSeconds(30));
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            await restartedSocket.WaitForSubscriptionAsync(timeout.Token);
        restartedSocket.Emit(BookMessage(scenario, scenario.YesTokenId));
        restartedSocket.Emit(BookMessage(scenario, scenario.NoTokenId));
        await WaitForStateAsync(
            restartedClient,
            clock,
            sessionId,
            "Running",
            "ReadyBeforeWindow");

        using var stopResponse = await restartedClient.PostAsync($"/api/Collector/{sessionId}/stop", null);
        stopResponse.EnsureSuccessStatusCode();
        await TickSchedulerAsync(restartedFactory.Services);
        AssertState(await GetSessionAsync(restartedClient, sessionId), "Failed", null);
    }

    [Fact]
    public async Task ConcurrentRegistration_ShouldCreateOneMarketAndOneScheduledJob()
    {
        var eventStartsAt = DateTimeOffset.Parse("2026-09-05T12:00:00Z");
        var clock = new FakeTimeProvider(eventStartsAt.AddSeconds(-90));
        var scenario = new AcceptanceScenario(eventStartsAt);
        await using var database = await fixture.CreateDatabaseAsync();
        await database.ApplyMigrationsAsync();
        await using var factory = new AcceptanceWebApplicationFactory(
            database.ConnectionString,
            clock,
            services => ConfigureScenario(
                services,
                scenario,
                new ControllableWebSocketFactory(new ControllableWebSocketConnection()),
                new NormalizationGate()));
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();

        var registrations = await Task.WhenAll(
            RegisterMarketResultAsync(firstClient, scenario),
            RegisterMarketResultAsync(secondClient, scenario));

        registrations.Select(result => result.MarketId).Distinct().Should().ContainSingle();
        registrations.Count(result => result.Created).Should().Be(1);
        var sessions = await GetSessionsAsync(firstClient);
        sessions.Where(session =>
                session["marketId"]!.GetValue<Guid>() == registrations[0].MarketId)
            .Should().ContainSingle();
    }

    [Fact]
    public async Task CancelScheduledJob_RestartAndExplicitReAdd_ShouldCreateNewAttempt()
    {
        var eventStartsAt = DateTimeOffset.Parse("2026-09-05T12:00:00Z");
        var clock = new FakeTimeProvider(eventStartsAt.AddSeconds(-90));
        var scenario = new AcceptanceScenario(eventStartsAt);
        await using var database = await fixture.CreateDatabaseAsync();
        await database.ApplyMigrationsAsync();

        Guid marketId;
        Guid cancelledSessionId;
        await using (var factory = new AcceptanceWebApplicationFactory(
            database.ConnectionString,
            clock,
            services => ConfigureScenario(
                services,
                scenario,
                new ControllableWebSocketFactory(new ControllableWebSocketConnection()),
                new NormalizationGate())))
        {
            using var client = factory.CreateClient();
            marketId = await RegisterMarketAsync(client, scenario);
            cancelledSessionId = await GetSessionIdByMarketAsync(client, marketId);
            using var stopResponse = await client.PostAsync(
                $"/api/Collector/{cancelledSessionId}/stop",
                null);
            stopResponse.EnsureSuccessStatusCode();
            await TickSchedulerAsync(factory.Services);
            var cancelled = await GetSessionAsync(client, cancelledSessionId);
            AssertState(cancelled, "Failed", null);
            cancelled["failureCode"]!.GetValue<string>().Should().Be("collector.stop.requested");
            cancelled["cleanup"].Should().NotBeNull();
        }

        await using var restartedFactory = new AcceptanceWebApplicationFactory(
            database.ConnectionString,
            clock,
            services => ConfigureScenario(
                services,
                scenario,
                new ControllableWebSocketFactory(new ControllableWebSocketConnection()),
                new NormalizationGate()));
        using var restartedClient = restartedFactory.CreateClient();
        AssertState(await GetSessionAsync(restartedClient, cancelledSessionId), "Failed", null);

        (await RegisterMarketAsync(restartedClient, scenario)).Should().Be(marketId);
        var replacementSessionId = await GetSessionIdByMarketAsync(restartedClient, marketId);
        replacementSessionId.Should().NotBe(cancelledSessionId);
        AssertState(
            await GetSessionAsync(restartedClient, replacementSessionId),
            "Scheduled",
            "WaitingForPreparation");

        using var replacementStopResponse = await restartedClient.PostAsync(
            $"/api/Collector/{replacementSessionId}/stop",
            null);
        replacementStopResponse.EnsureSuccessStatusCode();
        await TickSchedulerAsync(restartedFactory.Services);
    }

    [Fact]
    public async Task CancelOneRunningMarket_ShouldCleanupOnlyItsDatasetAndKeepNeighborCollecting()
    {
        var eventStartsAt = DateTimeOffset.Parse("2026-09-05T12:00:00Z");
        var clock = new FakeTimeProvider(eventStartsAt.AddSeconds(-60));
        var firstScenario = new AcceptanceScenario(eventStartsAt, "cancelled");
        var secondScenario = new AcceptanceScenario(eventStartsAt, "neighbor");
        var firstSocket = new ControllableWebSocketConnection();
        var secondSocket = new ControllableWebSocketConnection();
        var socketFactory = new ControllableWebSocketFactory(firstSocket, secondSocket);
        await using var database = await fixture.CreateDatabaseAsync();
        await database.ApplyMigrationsAsync();
        await using var factory = new AcceptanceWebApplicationFactory(
            database.ConnectionString,
            clock,
            services => ConfigureScenarios(
                services,
                [firstScenario, secondScenario],
                socketFactory,
                new NormalizationGate()));
        using var client = factory.CreateClient();

        var firstMarketId = await RegisterMarketAsync(client, firstScenario);
        var secondMarketId = await RegisterMarketAsync(client, secondScenario);
        var firstSessionId = await GetSessionIdByMarketAsync(client, firstMarketId);
        var secondSessionId = await GetSessionIdByMarketAsync(client, secondMarketId);
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            await firstSocket.WaitForSubscriptionAsync(timeout.Token);
            await secondSocket.WaitForSubscriptionAsync(timeout.Token);
        }

        var cancelledSocket = SocketFor(socketFactory, firstScenario);
        var neighborSocket = SocketFor(socketFactory, secondScenario);
        cancelledSocket.Emit(BookMessage(firstScenario, firstScenario.YesTokenId));
        cancelledSocket.Emit(BookMessage(firstScenario, firstScenario.NoTokenId));
        neighborSocket.Emit(BookMessage(secondScenario, secondScenario.YesTokenId));
        neighborSocket.Emit(BookMessage(secondScenario, secondScenario.NoTokenId));
        await WaitForStateAsync(client, clock, firstSessionId, "Running", "ReadyBeforeWindow");
        await WaitForStateAsync(client, clock, secondSessionId, "Running", "ReadyBeforeWindow");
        await WaitForRawCountAsync(client, firstSessionId, minimumCount: 2);
        await WaitForRawCountAsync(client, secondSessionId, minimumCount: 2);
        var neighborEvidenceBefore = await DurableCollectorAssertions.ReadAsync(
            database.ConnectionString,
            secondSessionId,
            projectionVersion: 1);

        using var stopResponse = await client.PostAsync($"/api/Collector/{firstSessionId}/stop", null);
        stopResponse.EnsureSuccessStatusCode();
        await TickSchedulerAsync(factory.Services);
        var cancelled = await GetSessionAsync(client, firstSessionId);
        AssertState(cancelled, "Failed", null);
        cancelled["cleanup"].Should().NotBeNull();
        AssertState(
            await GetSessionAsync(client, secondSessionId),
            "Running",
            "ReadyBeforeWindow");

        neighborSocket.Emit(BookMessage(secondScenario, secondScenario.YesTokenId));
        await WaitForRawCountAsync(
            client,
            secondSessionId,
            neighborEvidenceBefore.RawCount + 1);
        var neighborEvidenceAfter = await DurableCollectorAssertions.ReadAsync(
            database.ConnectionString,
            secondSessionId,
            projectionVersion: 1);
        neighborEvidenceAfter.RawCount.Should().BeGreaterThan(neighborEvidenceBefore.RawCount);

        using var neighborStopResponse = await client.PostAsync(
            $"/api/Collector/{secondSessionId}/stop",
            null);
        neighborStopResponse.EnsureSuccessStatusCode();
        await TickSchedulerAsync(factory.Services);
    }

    [Fact]
    public async Task ReadinessCompletedExactlyAtEventStart_ShouldFailAndCleanup()
    {
        var eventStartsAt = DateTimeOffset.Parse("2026-09-05T12:00:00Z");
        var clock = new FakeTimeProvider(eventStartsAt.AddMilliseconds(-1));
        var scenario = new AcceptanceScenario(eventStartsAt);
        var socket = new ControllableWebSocketConnection { AutoPong = false };
        await using var database = await fixture.CreateDatabaseAsync();
        await database.ApplyMigrationsAsync();
        await using var factory = new AcceptanceWebApplicationFactory(
            database.ConnectionString,
            clock,
            services => ConfigureScenario(
                services,
                scenario,
                new ControllableWebSocketFactory(socket),
                new NormalizationGate()));
        using var client = factory.CreateClient();

        var marketId = await RegisterMarketAsync(client, scenario);
        var sessionId = await GetSessionIdByMarketAsync(client, marketId);
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            await socket.WaitForSubscriptionAsync(timeout.Token);
        socket.Emit(BookMessage(scenario, scenario.YesTokenId));
        socket.Emit(BookMessage(scenario, scenario.NoTokenId));
        await WaitForStateAsync(
            client,
            clock,
            sessionId,
            "Starting",
            "AwaitingHeartbeat",
            advanceClock: false);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        socket.Emit("PONG");
        await Task.Delay(50);
        (await GetSessionAsync(client, sessionId))["status"]!.GetValue<string>()
            .Should().NotBe("Running");
        await TickSchedulerAsync(factory.Services);
        await TickSchedulerAsync(factory.Services);
        var failed = await GetSessionAsync(client, sessionId);
        AssertState(failed, "Failed", null);
        failed["cleanup"].Should().NotBeNull();
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
        var marketId = await RegisterMarketAsync(client, scenario);
        var sessionId = await GetSessionIdByMarketAsync(client, marketId);

        clock.Advance(TimeSpan.FromSeconds(30));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tick = await scope.ServiceProvider.GetRequiredService<ICollectorScheduler>()
                .TickAsync(CancellationToken.None);
            tick.IsSuccess.Should().BeTrue();
        }
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            await socket.WaitForSubscriptionAsync(timeout.Token);
        socket.Emit(BookMessage(scenario, scenario.YesTokenId));
        socket.Emit(BookMessage(scenario, scenario.NoTokenId));
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
                    scenario.ConditionId, scenario.YesTokenId,
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
                        Encoding.UTF8.GetBytes(BookMessage(scenario, scenario.YesTokenId)))],
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
        NormalizationGate normalizationGate) =>
        ConfigureScenarios(services, [scenario], socketFactory, normalizationGate);

    private static void ConfigureScenarios(
        IServiceCollection services,
        IReadOnlyCollection<AcceptanceScenario> scenarios,
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
            new ScenarioHttpMessageHandlerBuilderFilter(scenarios.ToArray()));
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

    private static async Task TickSchedulerAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICollectorScheduler>()
            .TickAsync(CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    private static async Task<Guid> RegisterMarketAsync(
        HttpClient client,
        AcceptanceScenario scenario) =>
        (await RegisterMarketResultAsync(client, scenario)).MarketId;

    private static async Task<MarketRegistrationResult> RegisterMarketResultAsync(
        HttpClient client,
        AcceptanceScenario scenario)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/Market",
            new { marketUri = $"https://polymarket.com/event/{scenario.EventSlug}" });
        response.EnsureSuccessStatusCode();
        var envelope = await response.ReadEnvelopeAsync();
        return new MarketRegistrationResult(
            envelope["result"]!["marketId"]!.GetValue<Guid>(),
            envelope["result"]!["created"]!.GetValue<bool>());
    }

    private static async Task<Guid> GetSessionIdByMarketAsync(HttpClient client, Guid marketId)
    {
        using var response = await client.GetAsync($"/api/Collector/by-market/{marketId}");
        response.EnsureSuccessStatusCode();
        var envelope = await response.ReadEnvelopeAsync();
        return envelope["result"]!["session"]!["sessionId"]!.GetValue<Guid>();
    }

    private static async Task<JsonObject> GetSessionAsync(HttpClient client, Guid sessionId)
    {
        using var response = await client.GetAsync($"/api/Collector/{sessionId}");
        response.EnsureSuccessStatusCode();
        var envelope = await response.ReadEnvelopeAsync();
        return envelope["result"]!["session"]!.AsObject();
    }

    private static async Task<IReadOnlyCollection<JsonObject>> GetSessionsAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/Collector");
        response.EnsureSuccessStatusCode();
        var envelope = await response.ReadEnvelopeAsync();
        return envelope["result"]!["sessions"]!.AsArray()
            .Select(session => session!.AsObject())
            .ToArray();
    }

    private static ControllableWebSocketConnection SocketFor(
        ControllableWebSocketFactory socketFactory,
        AcceptanceScenario scenario) =>
        socketFactory.Connections.Single(connection => connection.SentMessages.Any(message =>
            message.Contains(scenario.YesTokenId, StringComparison.Ordinal)
            && message.Contains(scenario.NoTokenId, StringComparison.Ordinal)));

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

    private static void AssertConsensusEvidence(DurableCollectorEvidence evidence)
    {
        evidence.UnexpectedNormalizationCount.Should().Be(0);
        evidence.MismatchedNormalizedEventCount.Should().Be(0);
        evidence.TerminalResolutionSourceCount.Should().Be(3);
        evidence.ConsensusReferenceCount.Should().Be(1);
    }

    private static void AdvanceTo(FakeTimeProvider clock, DateTimeOffset target)
    {
        var delta = target - clock.GetUtcNow();
        if (delta > TimeSpan.Zero)
            clock.Advance(delta);
    }

    private static string BookMessage(AcceptanceScenario scenario, string tokenId) => $$"""
        {
          "event_type":"book",
          "market":"{{scenario.ConditionId}}",
          "asset_id":"{{tokenId}}",
          "hash":"hash-{{tokenId}}",
          "timestamp":"1788600000000",
          "bids":[],
          "asks":[]
        }
        """;

    private static string ResolutionMessage(AcceptanceScenario scenario) => $$"""
        {
          "event_type":"market_resolved",
          "id":"{{scenario.MarketId}}",
          "market":"{{scenario.ConditionId}}",
          "assets_ids":["{{scenario.YesTokenId}}","{{scenario.NoTokenId}}"],
          "winning_asset_id":"{{scenario.YesTokenId}}",
          "winning_outcome":"Yes",
          "timestamp":"1788600300000"
        }
        """;

    private sealed record MarketRegistrationResult(Guid MarketId, bool Created);
}
