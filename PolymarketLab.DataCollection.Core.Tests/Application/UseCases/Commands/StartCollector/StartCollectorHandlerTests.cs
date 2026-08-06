using CSharpFunctionalExtensions;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StartCollector;
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
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_WithInvalidCommand_ShouldReturnValidationError()
    {
        var fixture = new Fixture();

        var result = await fixture.HandleAsync(Guid.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Should().Be(StartCollectorErrors.MarketIdRequired);
        fixture.MarketSource.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithMissingMarket_ShouldReturnNotFound()
    {
        var fixture = new Fixture { Market = null };

        var result = await fixture.HandleAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Code.Should().Be("collector.start.market.not_found");
        fixture.Repository.TryAddCallCount.Should().Be(0);
        fixture.Runtime.StartCallCount.Should().Be(0);
    }

    [Theory]
    [InlineData(InvalidTokens.TooFew, "collector.start.tokens.insufficient")]
    [InlineData(InvalidTokens.EmptyOutcome, "collector.start.token.outcome.required")]
    [InlineData(InvalidTokens.DuplicateTokenId, "collector.start.token.id.duplicate")]
    [InlineData(InvalidTokens.DuplicateOutcomeIndex, "collector.start.token.outcome_index.duplicate")]
    public async Task Handle_WithInvalidTokens_ShouldReturnError(
        InvalidTokens invalidTokens,
        string expectedCode)
    {
        var fixture = new Fixture { Market = CreateMarket(invalidTokens) };

        var result = await fixture.HandleAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Code.Should().Be(expectedCode);
        fixture.Repository.TryAddCallCount.Should().Be(0);
        fixture.Runtime.StartCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithActiveSession_ShouldReturnExistingSession()
    {
        var fixture = new Fixture();
        var activeSession = CreateSession(fixture.MarketId);
        fixture.Repository.ActiveResults.Enqueue(activeSession);

        var result = await fixture.HandleAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.SessionId.Should().Be(activeSession.Id.Value);
        result.Value.Status.Should().Be("Starting");
        fixture.Repository.TryAddCallCount.Should().Be(0);
        fixture.Runtime.StartCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithNewSession_ShouldStartRuntimeAndMarkRunning()
    {
        var fixture = new Fixture();

        var result = await fixture.HandleAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.MarketId.Should().Be(fixture.MarketId.Value);
        result.Value.Status.Should().Be("Running");
        fixture.Repository.InsertedSession.Should().NotBeNull();
        fixture.Runtime.StartCallCount.Should().Be(1);
        fixture.Runtime.StartRequest!.Market.Should().BeSameAs(fixture.Market);
        fixture.Runtime.StartRequest.SessionId.Should().Be(fixture.Repository.InsertedSession!.Id);
        fixture.Repository.UpdateCalls.Should().ContainSingle();
        fixture.Repository.UpdateCalls[0].Status.Should().Be(CollectorSessionStatus.Running);
        fixture.Repository.UpdateCalls[0].CancellationToken.Should().Be(CancellationToken.None);
    }

    [Fact]
    public async Task Handle_WhenInsertLosesRace_ShouldReturnConcurrentActiveSession()
    {
        var fixture = new Fixture();
        var concurrentSession = CreateSession(fixture.MarketId);
        fixture.Repository.InsertResult = CollectorSessionInsertStatus.ActiveSessionConflict;
        fixture.Repository.ActiveResults.Enqueue(null);
        fixture.Repository.ActiveResults.Enqueue(concurrentSession);

        var result = await fixture.HandleAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.SessionId.Should().Be(concurrentSession.Id.Value);
        fixture.Runtime.StartCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WhenInsertRaceCannotBeResolved_ShouldReturnConflict()
    {
        var fixture = new Fixture();
        fixture.Repository.InsertResult = CollectorSessionInsertStatus.ActiveSessionConflict;
        fixture.Repository.ActiveResults.Enqueue(null);
        fixture.Repository.ActiveResults.Enqueue(null);

        var result = await fixture.HandleAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Should().Be(StartCollectorErrors.RaceUnresolved);
        fixture.Runtime.StartCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WhenRuntimeFails_ShouldMarkSessionFailedAndReturnRuntimeError()
    {
        var runtimeError = new Error(
            "collector.runtime.start.failed",
            "Runtime failed to start.",
            ErrorType.Failure);
        var fixture = new Fixture();
        fixture.Runtime.StartResult = UnitResult.Failure(runtimeError);

        var result = await fixture.HandleAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Should().Be(runtimeError);
        fixture.Repository.UpdateCalls.Should().ContainSingle();
        fixture.Repository.UpdateCalls[0].Status.Should().Be(CollectorSessionStatus.Failed);
        fixture.Repository.UpdateCalls[0].StopReason.Should().Be(CollectorStopReason.StartupFailure);
        fixture.Repository.UpdateCalls[0].FailureCode.Should().Be(runtimeError.Code);
        fixture.Repository.UpdateCalls[0].CancellationToken.Should().Be(CancellationToken.None);
        fixture.Runtime.StopCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WhenRuntimeStartIsCancelled_ShouldMarkSessionFailedAndPropagateCancellation()
    {
        var fixture = new Fixture();
        using var cancellationTokenSource = new CancellationTokenSource();
        fixture.Runtime.StartHandler = token =>
        {
            cancellationTokenSource.Cancel();
            return Task.FromCanceled<UnitResult<Error>>(token);
        };

        var action = () => fixture.HandleAsync(cancellationToken: cancellationTokenSource.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        fixture.Repository.UpdateCalls.Should().ContainSingle();
        fixture.Repository.UpdateCalls[0].Status.Should().Be(CollectorSessionStatus.Failed);
        fixture.Repository.UpdateCalls[0].FailureCode.Should()
            .Be(StartCollectorErrors.RuntimeStartCancelled.Code);
        fixture.Repository.UpdateCalls[0].CancellationToken.Should().Be(CancellationToken.None);
    }

    [Fact]
    public async Task Handle_WhenRunningStateCannotBeSaved_ShouldStopRuntimeAndMarkFailed()
    {
        var persistenceError = new Error(
            "collector.session.update.failed",
            "Session state could not be saved.",
            ErrorType.Failure);
        var fixture = new Fixture();
        fixture.Repository.UpdateResults.Enqueue(
            Result.Failure<CollectorSessionUpdateStatus, Error>(persistenceError));
        fixture.Repository.UpdateResults.Enqueue(CollectorSessionUpdateStatus.Updated);

        var result = await fixture.HandleAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Should().Be(persistenceError);
        fixture.Runtime.StopCallCount.Should().Be(1);
        fixture.Runtime.StopCancellationToken.Should().Be(CancellationToken.None);
        fixture.Repository.UpdateCalls.Should().HaveCount(2);
        fixture.Repository.UpdateCalls[0].Status.Should().Be(CollectorSessionStatus.Running);
        fixture.Repository.UpdateCalls[1].Status.Should().Be(CollectorSessionStatus.Failed);
        fixture.Repository.UpdateCalls[1].StopReason.Should().Be(CollectorStopReason.PersistenceFailure);
    }

    [Fact]
    public async Task Handle_WhenRuntimeFailureWinsRunningUpdate_ShouldNotResurrectSession()
    {
        var runtimeError = new Error(
            "collector.runtime.receive.closed",
            "Remote endpoint closed the connection.",
            ErrorType.Failure);
        var fixture = new Fixture();
        fixture.Repository.UpdateResults.Enqueue(
            CollectorSessionUpdateStatus.ConcurrencyConflict);
        fixture.Repository.GetByIdHandler = sessionId =>
        {
            var persisted = CollectorSessionAggregate.Create(
                sessionId,
                fixture.MarketId,
                Now).Value;
            persisted.Fail(
                Now,
                CollectorStopReason.FatalWebSocketError,
                runtimeError.Code,
                runtimeError.Message);
            return persisted;
        };

        var result = await fixture.HandleAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Should().Be(runtimeError);
        fixture.Runtime.StopCallCount.Should().Be(1);
        fixture.Repository.UpdateCalls.Should().ContainSingle();
        fixture.Repository.UpdateCalls[0].ExpectedStatus.Should()
            .Be(CollectorSessionStatus.Starting);
    }

    private static CollectionMarket CreateMarket(InvalidTokens invalidTokens = InvalidTokens.None)
    {
        var marketId = MarketId.Create(Guid.NewGuid()).Value;
        var yesToken = TokenId.Create("token-yes").Value;
        var noToken = invalidTokens == InvalidTokens.DuplicateTokenId
            ? yesToken
            : TokenId.Create("token-no").Value;

        IReadOnlyCollection<CollectionMarketToken> tokens = invalidTokens switch
        {
            InvalidTokens.TooFew => [new CollectionMarketToken(yesToken, "Yes", 0)],
            InvalidTokens.EmptyOutcome =>
            [
                new CollectionMarketToken(yesToken, " ", 0),
                new CollectionMarketToken(noToken, "No", 1)
            ],
            InvalidTokens.DuplicateOutcomeIndex =>
            [
                new CollectionMarketToken(yesToken, "Yes", 0),
                new CollectionMarketToken(noToken, "No", 0)
            ],
            _ =>
            [
                new CollectionMarketToken(yesToken, "Yes", 0),
                new CollectionMarketToken(noToken, "No", 1)
            ]
        };

        return new CollectionMarket(marketId, "will-it-rain", tokens);
    }

    private static CollectorSessionAggregate CreateSession(MarketId marketId)
    {
        return CollectorSessionAggregate.Create(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            marketId,
            Now).Value;
    }

    public enum InvalidTokens
    {
        None,
        TooFew,
        EmptyOutcome,
        DuplicateTokenId,
        DuplicateOutcomeIndex
    }

    private sealed class Fixture
    {
        private CollectionMarket? _market = CreateMarket();

        public Fixture()
        {
            MarketSource = new StubMarketSource(() => _market);
            Handler = new StartCollectorHandler(
                new StartCollectorValidator(),
                MarketSource,
                Repository,
                Runtime,
                new FixedTimeProvider(Now));
        }

        public StartCollectorHandler Handler { get; }
        public StubMarketSource MarketSource { get; }
        public StubCollectorSessionRepository Repository { get; } = new();
        public StubCollectorRuntime Runtime { get; } = new();

        public CollectionMarket? Market
        {
            get => _market;
            init => _market = value;
        }

        public MarketId MarketId => _market?.MarketId
            ?? PolymarketLab.SharedKernel.DomainModels.Ids.MarketId.Create(Guid.NewGuid()).Value;

        public Task<Result<StartCollectorResponse, Error.ErrorList>> HandleAsync(
            Guid? marketId = null,
            CancellationToken cancellationToken = default)
        {
            return Handler.Handle(
                new StartCollectorCommand(marketId ?? MarketId.Value),
                cancellationToken);
        }
    }

    private sealed class StubMarketSource(Func<CollectionMarket?> marketFactory)
        : IMarketCollectionSource
    {
        public int CallCount { get; private set; }

        public Task<CollectionMarket?> GetByIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var market = marketFactory();
            return Task.FromResult(market?.MarketId.Equals(marketId) == true ? market : null);
        }
    }

    private sealed class StubCollectorSessionRepository : ICollectorSessionRepository
    {
        public Queue<CollectorSessionAggregate?> ActiveResults { get; } = [];
        public Queue<Result<CollectorSessionUpdateStatus, Error>> UpdateResults { get; } = [];
        public List<UpdateCall> UpdateCalls { get; } = [];
        public Func<CollectorSessionId, CollectorSessionAggregate?>? GetByIdHandler { get; set; }
        public CollectorSessionInsertStatus InsertResult { get; set; }
            = CollectorSessionInsertStatus.Inserted;
        public CollectorSessionAggregate? InsertedSession { get; private set; }
        public int TryAddCallCount { get; private set; }

        public Task<CollectorSessionAggregate?> GetActiveByMarketIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                ActiveResults.TryDequeue(out var session) ? session : null);
        }

        public Task<CollectorSessionAggregate?> GetCurrentByMarketIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<CollectorSessionInsertStatus, Error>> TryAddAsync(
            CollectorSessionAggregate session,
            CancellationToken cancellationToken)
        {
            TryAddCallCount++;
            InsertedSession = session;
            return Task.FromResult<Result<CollectorSessionInsertStatus, Error>>(InsertResult);
        }

        public Task<Result<CollectorSessionUpdateStatus, Error>> TryUpdateAsync(
            CollectorSessionAggregate session,
            CollectorSessionStatus expectedStatus,
            CancellationToken cancellationToken)
        {
            UpdateCalls.Add(new UpdateCall(
                session.Status,
                expectedStatus,
                session.StopReason,
                session.FailureCode,
                session.FailureMessage,
                cancellationToken));

            return Task.FromResult(
                UpdateResults.TryDequeue(out var result)
                    ? result
                    : Result.Success<CollectorSessionUpdateStatus, Error>(
                        CollectorSessionUpdateStatus.Updated));
        }

        public Task<CollectorSessionAggregate?> GetByIdAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(GetByIdHandler?.Invoke(sessionId));
        }

        public Task<IReadOnlyCollection<CollectorSessionAggregate>> GetActiveAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubCollectorRuntime : ICollectorRuntime
    {
        public UnitResult<Error> StartResult { get; set; } = UnitResult.Success<Error>();
        public UnitResult<Error> StopResult { get; set; } = UnitResult.Success<Error>();
        public Func<CancellationToken, Task<UnitResult<Error>>>? StartHandler { get; set; }
        public CollectorRuntimeStartRequest? StartRequest { get; private set; }
        public int StartCallCount { get; private set; }
        public int StopCallCount { get; private set; }
        public CancellationToken StopCancellationToken { get; private set; }

        public Task<UnitResult<Error>> StartAsync(
            CollectorRuntimeStartRequest request,
            CancellationToken cancellationToken)
        {
            StartCallCount++;
            StartRequest = request;
            return StartHandler?.Invoke(cancellationToken) ?? Task.FromResult(StartResult);
        }

        public Task<UnitResult<Error>> StopAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken)
        {
            StopCallCount++;
            StopCancellationToken = cancellationToken;
            return Task.FromResult(StopResult);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed record UpdateCall(
        CollectorSessionStatus Status,
        CollectorSessionStatus ExpectedStatus,
        CollectorStopReason? StopReason,
        string? FailureCode,
        string? FailureMessage,
        CancellationToken CancellationToken);
}
