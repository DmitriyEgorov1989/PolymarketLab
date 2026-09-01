using CSharpFunctionalExtensions;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorScheduling;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionInvalidation;
using PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StartCollector;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Tests.Application.UseCases.Commands.StartCollector;

public sealed class StartCollectorHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 27, 11, 57, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_WithExistingSessionForSameMarket_ShouldReturnItWithoutGammaRequest()
    {
        var fixture = new Fixture();
        var existing = CreateSession(fixture.Market!);
        fixture.Repository.ExclusiveResults.Enqueue(existing);

        var result = await fixture.HandleAsync(fixture.Market!.MarketId.Value);

        result.IsSuccess.Should().BeTrue();
        result.Value.SessionId.Should().Be(existing.Id.Value);
        result.Value.Status.Should().Be("Scheduled");
        fixture.MarketSource.CallCount.Should().Be(0);
        fixture.Repository.TryAddCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithExistingSessionForDifferentMarket_ShouldReturnGlobalConflictBeforeGamma()
    {
        var fixture = new Fixture();
        fixture.Repository.ExclusiveResults.Enqueue(
            CreateSession(CreateMarket(MarketId.Create(Guid.NewGuid()).Value)));

        var result = await fixture.HandleAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Should().Be(StartCollectorErrors.GlobalSessionConflict);
        fixture.MarketSource.CallCount.Should().Be(0);
        fixture.Repository.TryAddCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithNewMarket_ShouldPersistExactScheduledSnapshot()
    {
        var fixture = new Fixture(projectionVersion: 3)
        {
            Market = CreateMarket() with { AcceptingOrders = false }
        };

        var result = await fixture.HandleAsync(fixture.Market!.MarketId.Value);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Scheduled");
        var session = fixture.Repository.InsertedSession!;
        session.Status.Should().Be(CollectorSessionStatus.Scheduled);
        session.Phase.Should().Be(CollectorSessionPhase.WaitingForPreparation);
        session.ExternalEventId.Should().Be(fixture.Market.ExternalEventId);
        session.EventSlug.Should().Be(fixture.Market.EventSlug);
        session.ExternalMarketId.Should().Be(fixture.Market.ExternalMarketId);
        session.MarketSlug.Should().Be(fixture.Market.MarketSlug);
        session.ConditionId.Should().Be(fixture.Market.ConditionId);
        session.EventStartsAt.Should().Be(fixture.Market.EventStartsAt);
        session.EventEndsAt.Should().Be(fixture.Market.EventEndsAt);
        session.ProjectionVersion.Should().Be(3);
        session.Tokens.Select(token => (token.TokenId, token.Outcome, token.OutcomeIndex))
            .Should()
            .Equal(
                (fixture.Market.Tokens[0].TokenId, "Yes", 0),
                (fixture.Market.Tokens[1].TokenId, "No", 1));
    }

    [Fact]
    public async Task Handle_AtPreparationBoundary_ShouldStartRuntimeWithRegularDeadline()
    {
        var fixture = new Fixture(now: Now.AddMinutes(2));

        var result = await fixture.HandleAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Starting");
        fixture.Runtime.StartRequests.Should().ContainSingle();
        fixture.Runtime.StartRequests.Single().ReadinessDeadline.Should()
            .Be(fixture.Market!.EventStartsAt.AddSeconds(-10));
    }

    [Fact]
    public async Task Handle_AtLatePreparationBoundary_ShouldUseEventStartAsDeadline()
    {
        var fixture = new Fixture(now: Now.AddMinutes(2).AddSeconds(50));

        var result = await fixture.HandleAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Starting");
        fixture.Runtime.StartRequests.Single().ReadinessDeadline.Should()
            .Be(fixture.Market!.EventStartsAt);
    }

    [Fact]
    public async Task Handle_WhenPersistedMarketIsAlreadyOpen_ShouldRejectBeforeGamma()
    {
        var fixture = new Fixture(now: Now.AddMinutes(3));

        var result = await fixture.HandleAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Should().Be(
            StartCollectorErrors.MarketAlreadyOpen(fixture.RequestedMarketId.Value));
        fixture.MarketSource.CallCount.Should().Be(0);
        fixture.Repository.TryAddCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WhenMarketOpensDuringGammaRequest_ShouldRejectWithoutSession()
    {
        var marketStartsAt = Now.AddMinutes(3);
        var fixture = new Fixture(timeProvider: new SequenceTimeProvider(
            marketStartsAt.AddMilliseconds(-1),
            marketStartsAt));

        var result = await fixture.HandleAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Should().Be(
            StartCollectorErrors.MarketAlreadyOpen(fixture.RequestedMarketId.Value));
        fixture.MarketSource.CallCount.Should().Be(1);
        fixture.Repository.TryAddCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WhenGammaFails_ShouldPreserveIntegrationError()
    {
        var integrationError = new Error(
            "market.collection.gamma.unavailable",
            "Gamma request failed.",
            ErrorType.Failure);
        var fixture = new Fixture { MarketError = integrationError };

        var result = await fixture.HandleAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Should().Be(integrationError);
        fixture.Repository.TryAddCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WhenInsertLosesRaceToSameMarket_ShouldReturnWinner()
    {
        var fixture = new Fixture();
        var winner = CreateSession(fixture.Market!);
        fixture.Repository.InsertResult = CollectorSessionInsertStatus.ExclusiveSessionConflict;
        fixture.Repository.ExclusiveResults.Enqueue(null);
        fixture.Repository.ExclusiveResults.Enqueue(winner);

        var result = await fixture.HandleAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.SessionId.Should().Be(winner.Id.Value);
    }

    [Fact]
    public async Task Handle_WhenInsertLosesRaceToDifferentMarket_ShouldReturnGlobalConflict()
    {
        var fixture = new Fixture();
        var winnerMarket = CreateMarket(MarketId.Create(Guid.NewGuid()).Value);
        fixture.Repository.InsertResult = CollectorSessionInsertStatus.ExclusiveSessionConflict;
        fixture.Repository.ExclusiveResults.Enqueue(null);
        fixture.Repository.ExclusiveResults.Enqueue(CreateSession(winnerMarket));

        var result = await fixture.HandleAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Should().Be(StartCollectorErrors.GlobalSessionConflict);
    }

    [Fact]
    public async Task Handle_WhenInsertRaceCannotBeResolved_ShouldReturnConflict()
    {
        var fixture = new Fixture();
        fixture.Repository.InsertResult = CollectorSessionInsertStatus.ExclusiveSessionConflict;
        fixture.Repository.ExclusiveResults.Enqueue(null);
        fixture.Repository.ExclusiveResults.Enqueue(null);

        var result = await fixture.HandleAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Should().Be(StartCollectorErrors.RaceUnresolved);
    }

    [Fact]
    public async Task Handle_WithMissingMarket_ShouldReturnNotFound()
    {
        var fixture = new Fixture { Market = null };

        var result = await fixture.HandleAsync(fixture.RequestedMarketId.Value);

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Code.Should().Be("collector.start.market.not_found");
        fixture.Repository.TryAddCallCount.Should().Be(0);
    }

    private static CollectionMarket CreateMarket(MarketId? marketId = null) =>
        new(
            marketId ?? MarketId.Create(Guid.NewGuid()).Value,
            "event-123",
            "btc-updown-5m-1200",
            "market-123",
            "btc-updown-5m-1200",
            "0xabc",
            Now.AddMinutes(3),
            Now.AddMinutes(8),
            true,
            false,
            true,
            true,
            [
                new CollectionMarketToken(TokenId.Create("1001").Value, "Yes", 0),
                new CollectionMarketToken(TokenId.Create("1002").Value, "No", 1)
            ]);

    private static CollectorSessionAggregate CreateSession(CollectionMarket market) =>
        CollectorSessionAggregate.Create(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            market.MarketId,
            market.ExternalEventId,
            market.EventSlug,
            market.ExternalMarketId,
            market.MarketSlug,
            market.ConditionId,
            market.EventStartsAt,
            market.EventEndsAt,
            3,
            market.Tokens.Select(token => new CollectorSessionTokenDefinition(
                token.TokenId,
                token.Outcome,
                token.OutcomeIndex)).ToArray(),
            Now).Value;

    private sealed class Fixture
    {
        private CollectionMarket? _market = CreateMarket();

        public Fixture(
            int projectionVersion = 3,
            DateTimeOffset? now = null,
            TimeProvider? timeProvider = null)
        {
            RequestedMarketId = _market!.MarketId;
            MarketSource = new StubMarketSource(() => _market, () => MarketError);
            var actualTimeProvider = timeProvider ?? new FixedTimeProvider(now ?? Now);
            var scheduler = new CollectorScheduler(
                MarketSource,
                Repository,
                Runtime,
                new CollectorSessionInvalidationCoordinator(Repository, Runtime),
                new CollectorBoundaryCheckRegistry(),
                actualTimeProvider);
            Handler = new StartCollectorHandler(
                MarketSource,
                Repository,
                new StubProjectionVersionProvider(projectionVersion),
                scheduler,
                actualTimeProvider);
        }

        public StartCollectorHandler Handler { get; }
        public StubMarketSource MarketSource { get; }
        public StubCollectorSessionRepository Repository { get; } = new();
        public StubCollectorRuntime Runtime { get; } = new();
        public MarketId RequestedMarketId { get; }
        public Error? MarketError { get; init; }

        public CollectionMarket? Market
        {
            get => _market;
            init => _market = value;
        }

        public Task<Result<StartCollectorResponse, Error.ErrorList>> HandleAsync(
            Guid? marketId = null,
            CancellationToken cancellationToken = default) =>
            Handler.Handle(
                new StartCollectorCommand(marketId ?? RequestedMarketId.Value),
                cancellationToken);
    }

    private sealed class StubMarketSource(
        Func<CollectionMarket?> marketFactory,
        Func<Error?> errorFactory) : IMarketCollectionSource
    {
        public int CallCount { get; private set; }

        public Task<CollectionMarketWindow?> GetWindowAsync(
            MarketId marketId,
            CancellationToken cancellationToken)
        {
            var market = marketFactory();
            return Task.FromResult(
                market?.MarketId == marketId
                    ? new CollectionMarketWindow(market.MarketId, market.EventStartsAt)
                    : null);
        }

        public Task<Result<CollectionMarket?, Error>> GetByIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var error = errorFactory();
            if (error is not null)
                return Task.FromResult(Result.Failure<CollectionMarket?, Error>(error));

            var market = marketFactory();
            return Task.FromResult<Result<CollectionMarket?, Error>>(
                market?.MarketId == marketId ? market : null);
        }
    }

    private sealed class StubCollectorSessionRepository : ICollectorSessionRepository
    {
        public Queue<CollectorSessionAggregate?> ExclusiveResults { get; } = [];
        public CollectorSessionInsertStatus InsertResult { get; set; } =
            CollectorSessionInsertStatus.Inserted;
        public CollectorSessionAggregate? InsertedSession { get; private set; }
        public int TryAddCallCount { get; private set; }

        public Task<CollectorSessionAggregate?> GetExclusiveAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(
                ExclusiveResults.TryDequeue(out var session) ? session : null);

        public Task<Result<CollectorSessionInsertStatus, Error>> TryAddAsync(
            CollectorSessionAggregate session,
            CancellationToken cancellationToken)
        {
            TryAddCallCount++;
            InsertedSession = session;
            return Task.FromResult<Result<CollectorSessionInsertStatus, Error>>(InsertResult);
        }

        public Task<CollectorSessionAggregate?> GetByIdAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CollectorSessionAggregate?> GetActiveByMarketIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CollectorSessionAggregate?> GetCurrentByMarketIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyCollection<CollectorSessionAggregate>> GetActiveAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<CollectorSessionUpdateStatus, Error>> TryUpdateAsync(
            CollectorSessionAggregate session,
            CollectorSessionStatus expectedStatus,
            CancellationToken cancellationToken) =>
            Task.FromResult<Result<CollectorSessionUpdateStatus, Error>>(
                CollectorSessionUpdateStatus.Updated);
    }

    private sealed class StubCollectorRuntime : ICollectorRuntime
    {
        public List<CollectorRuntimeStartRequest> StartRequests { get; } = [];

        public void FenceSession(CollectorSessionId sessionId)
        {
        }

        public Task<UnitResult<Error>> StartAsync(
            CollectorRuntimeStartRequest request,
            CancellationToken cancellationToken)
        {
            StartRequests.Add(request);
            return Task.FromResult(UnitResult.Success<Error>());
        }

        public Task<UnitResult<Error>> StopAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(UnitResult.Success<Error>());
    }

    private sealed class StubProjectionVersionProvider(int projectionVersion)
        : IProjectionVersionProvider
    {
        public int ProjectionVersion { get; } = projectionVersion;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SequenceTimeProvider(params DateTimeOffset[] values) : TimeProvider
    {
        private readonly Queue<DateTimeOffset> _values = new(values);
        private DateTimeOffset _last = values[^1];

        public override DateTimeOffset GetUtcNow()
        {
            if (_values.TryDequeue(out var value))
                _last = value;
            return _last;
        }
    }
}
