using CSharpFunctionalExtensions;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorNormalizationSuitability;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorRawDatasetCompletion;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionInvalidation;
using PolymarketLab.DataCollection.Core.Application.UseCases.ResolutionConsensus;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.DataCollection.Core.Tests.TestSupport;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Tests.Application.UseCases.ResolutionConsensus;

public sealed class ResolutionConsensusCoordinatorTests
{
    private static readonly DateTimeOffset CreatedAt =
        DateTimeOffset.Parse("2026-09-05T11:57:00Z");

    [Fact]
    public async Task TickAsync_AtExactEventStart_ShouldEnterCollectingWindow()
    {
        var fixture = new Fixture(CreateRunningSession());
        fixture.Time.SetUtcNow(fixture.Session.EventStartsAt!.Value);

        var result = await fixture.Coordinator.TickAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Session.Phase.Should().Be(CollectorSessionPhase.CollectingWindow);
        fixture.RawCompletion.CallCount.Should().Be(0);
        fixture.Sessions.ExpectedStatuses.Should().Equal(CollectorSessionStatus.Running);
    }

    [Fact]
    public async Task TickAsync_AtExactEventEnd_ShouldStartRawCompletionWithoutResolution()
    {
        var session = CreateRunningSession();
        session.MarkCollectingWindow().IsSuccess.Should().BeTrue();
        var fixture = new Fixture(session);
        fixture.Time.SetUtcNow(session.EventEndsAt!.Value);

        var result = await fixture.Coordinator.TickAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.RawCompletion.SessionIds.Should().Equal(session.Id);
        fixture.Invalidation.Calls.Should().BeEmpty();
        session.ResolutionConfirmedAt.Should().BeNull();
    }

    [Fact]
    public async Task TickAsync_WithLegacyAwaitingResolution_ShouldStartRawCompletionWithoutPolling()
    {
        var session = CreateRunningSession();
        session.MarkCollectingWindow().IsSuccess.Should().BeTrue();
        session.MarkAwaitingResolution().IsSuccess.Should().BeTrue();
        var fixture = new Fixture(session);
        fixture.Time.SetUtcNow(session.EventEndsAt!.Value.AddSeconds(1));

        var result = await fixture.Coordinator.TickAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.RawCompletion.SessionIds.Should().Equal(session.Id);
        fixture.Invalidation.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task TickAsync_WhenAwaitingNormalization_ShouldEvaluateSuitability()
    {
        var session = CreateRunningSession();
        session.MarkCollectingWindow().IsSuccess.Should().BeTrue();
        session.MarkStopping().IsSuccess.Should().BeTrue();
        session.MarkAwaitingNormalization(session.EventEndsAt!.Value)
            .IsSuccess.Should().BeTrue();
        var fixture = new Fixture(session);

        var result = await fixture.Coordinator.TickAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Suitability.SessionIds.Should().Equal(session.Id);
        fixture.RawCompletion.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task TickAsync_WithInvalidSnapshot_ShouldInvalidateAndStopRuntime()
    {
        var session = CreateRunningSession();
        SetValue<DateTimeOffset?>(
            session,
            nameof(CollectorSessionAggregate.EventEndsAt),
            null);
        var fixture = new Fixture(session);
        fixture.Time.SetUtcNow(session.EventStartsAt!.Value);

        var result = await fixture.Coordinator.TickAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Invalidation.Calls.Should().ContainSingle(call =>
            call.Reason == CollectorStopReason.PersistenceFailure
            && call.Failure.Code == "collector.lifecycle.snapshot_invalid");
        fixture.Runtime.StopSessionIds.Should().Equal(session.Id);
    }

    private static CollectorSessionAggregate CreateRunningSession() =>
        CollectorSessionTestFactory.CreateRunning(createdAt: CreatedAt);

    private static void SetValue<T>(
        CollectorSessionAggregate session,
        string propertyName,
        T value)
    {
        typeof(CollectorSessionAggregate)
            .GetProperty(propertyName)!
            .SetValue(session, value);
    }

    private sealed class Fixture
    {
        public Fixture(CollectorSessionAggregate session)
        {
            Session = session;
            Sessions = new SessionRepository(session);
            Runtime = new CollectorRuntime();
            Invalidation = new InvalidationCoordinator(session);
            RawCompletion = new RawCompletionCoordinator();
            Suitability = new SuitabilityCoordinator();
            Time = new MutableTimeProvider(session.EventEndsAt ?? CreatedAt);
            Coordinator = new ResolutionConsensusCoordinator(
                Sessions,
                Runtime,
                Invalidation,
                RawCompletion,
                Suitability,
                Time);
        }

        public CollectorSessionAggregate Session { get; }
        public SessionRepository Sessions { get; }
        public CollectorRuntime Runtime { get; }
        public InvalidationCoordinator Invalidation { get; }
        public RawCompletionCoordinator RawCompletion { get; }
        public SuitabilityCoordinator Suitability { get; }
        public MutableTimeProvider Time { get; }
        public IResolutionConsensusCoordinator Coordinator { get; }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
    }

    private sealed class RawCompletionCoordinator : ICollectorRawDatasetCompletionCoordinator
    {
        public List<CollectorSessionId> SessionIds { get; } = [];
        public int CallCount => SessionIds.Count;

        public Task<UnitResult<Error>> CompleteAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken)
        {
            SessionIds.Add(sessionId);
            return Task.FromResult(UnitResult.Success<Error>());
        }
    }

    private sealed class SuitabilityCoordinator : ICollectorNormalizationSuitabilityCoordinator
    {
        public List<CollectorSessionId> SessionIds { get; } = [];

        public Task<UnitResult<Error>> EvaluateAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken)
        {
            SessionIds.Add(sessionId);
            return Task.FromResult(UnitResult.Success<Error>());
        }
    }

    private sealed class CollectorRuntime : ICollectorRuntime
    {
        public List<CollectorSessionId> StopSessionIds { get; } = [];

        public void FenceSession(CollectorSessionId sessionId)
        {
        }

        public Task<UnitResult<Error>> StartAsync(
            CollectorRuntimeStartRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UnitResult<Error>> StopAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken)
        {
            StopSessionIds.Add(sessionId);
            return Task.FromResult(UnitResult.Success<Error>());
        }

    }

    private sealed class InvalidationCoordinator(CollectorSessionAggregate session)
        : ICollectorSessionInvalidationCoordinator
    {
        public List<(CollectorStopReason Reason, Error Failure)> Calls { get; } = [];

        public Task<Result<CollectorSessionAggregate?, Error>> InvalidateAsync(
            CollectorSessionId sessionId,
            DateTimeOffset occurredAt,
            CollectorStopReason reason,
            Error failure,
            CancellationToken cancellationToken)
        {
            Calls.Add((reason, failure));
            return Task.FromResult(
                Result.Success<CollectorSessionAggregate?, Error>(session));
        }
    }

    private sealed class SessionRepository(CollectorSessionAggregate session)
        : ICollectorSessionRepository
    {
        public List<CollectorSessionStatus> ExpectedStatuses { get; } = [];

        public Task<CollectorSessionAggregate?> GetByIdAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CollectorSessionAggregate?>(session);

        public Task<IReadOnlyCollection<CollectorSessionAggregate>> GetActiveAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<CollectorSessionAggregate>>([session]);

        public Task<Result<CollectorSessionUpdateStatus, Error>> TryUpdateAsync(
            CollectorSessionAggregate current,
            CollectorSessionStatus expectedStatus,
            CancellationToken cancellationToken)
        {
            ExpectedStatuses.Add(expectedStatus);
            return Task.FromResult(Result.Success<CollectorSessionUpdateStatus, Error>(
                CollectorSessionUpdateStatus.Updated));
        }

        public Task<CollectorSessionAggregate?> GetExclusiveAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CollectorSessionAggregate?> GetActiveByMarketIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CollectorSessionAggregate?> GetCurrentByMarketIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<CollectorSessionInsertStatus, Error>> TryAddAsync(
            CollectorSessionAggregate current,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
