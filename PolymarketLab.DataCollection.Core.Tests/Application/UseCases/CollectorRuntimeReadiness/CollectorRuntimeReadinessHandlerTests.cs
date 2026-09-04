using CSharpFunctionalExtensions;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorRuntimeReadiness;
using PolymarketLab.DataCollection.Core.Application.UseCases.CollectorSessionInvalidation;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.DataCollection.Core.Tests.TestSupport;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Tests.Application.UseCases.CollectorRuntimeReadiness;

public sealed class CollectorRuntimeReadinessHandlerTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 28, 11, 57, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EnqueuedAt =
        new(2026, 8, 28, 11, 59, 44, TimeSpan.Zero);

    [Fact]
    public async Task RecordInitialBookEnqueuedAsync_WithAwaitingInitialBooks_ShouldPersistObservation()
    {
        var session = CreateAwaitingInitialBooks();
        var repository = new StubCollectorSessionRepository(session);
        var readinessRepository = new StubCollectorTokenReadinessRepository();
        var handler = CreateHandler(repository, readinessRepository);
        var tokenId = TokenId.Create("1001").Value;

        var result = await handler.RecordInitialBookEnqueuedAsync(
            session.Id,
            tokenId,
            1,
            EnqueuedAt,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        readinessRepository.Recorded.Should().ContainSingle().Which.Should()
            .Be(new CollectorTokenReadiness(session.Id, 1, tokenId, EnqueuedAt));
    }

    [Fact]
    public async Task RecordInitialBookEnqueuedAsync_WithMissingSession_ShouldBeIdempotent()
    {
        var sessionId = CollectorSessionId.Create(Guid.NewGuid()).Value;
        var readinessRepository = new StubCollectorTokenReadinessRepository();
        var handler = CreateHandler(
            new StubCollectorSessionRepository(),
            readinessRepository);

        var result = await handler.RecordInitialBookEnqueuedAsync(
            sessionId,
            TokenId.Create("1001").Value,
            1,
            EnqueuedAt,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        readinessRepository.Recorded.Should().BeEmpty();
    }

    [Theory]
    [InlineData(CollectorSessionStatus.Running)]
    [InlineData(CollectorSessionStatus.Stopping)]
    [InlineData(CollectorSessionStatus.Stopped)]
    [InlineData(CollectorSessionStatus.Failed)]
    public async Task RecordInitialBookEnqueuedAsync_WithNonAwaitingStatus_ShouldBeIdempotent(
        CollectorSessionStatus status)
    {
        var session = CreateAwaitingInitialBooks();
        if (status != CollectorSessionStatus.Starting)
        {
            session.MarkAwaitingHeartbeat();
            session.MarkRunning(CreatedAt.AddSeconds(2));
        }

        if (status == CollectorSessionStatus.Stopping)
            session.MarkStopping();
        else if (status == CollectorSessionStatus.Stopped)
            session.Stop(CreatedAt.AddSeconds(3), CollectorStopReason.Requested);
        else if (status == CollectorSessionStatus.Failed)
            session.Fail(
                CreatedAt.AddSeconds(3),
                CollectorStopReason.PersistenceFailure,
                "collector.runtime.persist.failed",
                "Persistence failed.");

        var readinessRepository = new StubCollectorTokenReadinessRepository();
        var handler = CreateHandler(
            new StubCollectorSessionRepository(session),
            readinessRepository);

        var result = await handler.RecordInitialBookEnqueuedAsync(
            session.Id,
            TokenId.Create("1001").Value,
            1,
            EnqueuedAt,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        readinessRepository.Recorded.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordInitialBookEnqueuedAsync_WithAwaitingHeartbeatPhase_ShouldBeIdempotent()
    {
        var session = CreateAwaitingInitialBooks();
        session.MarkAwaitingHeartbeat();
        var readinessRepository = new StubCollectorTokenReadinessRepository();
        var handler = CreateHandler(
            new StubCollectorSessionRepository(session),
            readinessRepository);

        var result = await handler.RecordInitialBookEnqueuedAsync(
            session.Id,
            TokenId.Create("1001").Value,
            1,
            EnqueuedAt,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        readinessRepository.Recorded.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordInitialBookEnqueuedAsync_WithTokenOutsideSnapshot_ShouldReturnSafeFailure()
    {
        var session = CreateAwaitingInitialBooks();
        var readinessRepository = new StubCollectorTokenReadinessRepository();
        var handler = CreateHandler(
            new StubCollectorSessionRepository(session),
            readinessRepository);
        var unknownTokenId = TokenId.Create("9999").Value;

        var result = await handler.RecordInitialBookEnqueuedAsync(
            session.Id,
            unknownTokenId,
            1,
            EnqueuedAt,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.runtime.readiness.token.unknown");
        readinessRepository.Recorded.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RecordInitialBookEnqueuedAsync_WithNonPositiveEpoch_ShouldReturnSafeFailure(
        long epoch)
    {
        var session = CreateAwaitingInitialBooks();
        var readinessRepository = new StubCollectorTokenReadinessRepository();
        var handler = CreateHandler(
            new StubCollectorSessionRepository(session),
            readinessRepository);

        var result = await handler.RecordInitialBookEnqueuedAsync(
            session.Id,
            TokenId.Create("1001").Value,
            epoch,
            EnqueuedAt,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        readinessRepository.Recorded.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordInitialBookEnqueuedAsync_WithDefaultEnqueuedAt_ShouldReturnSafeFailure()
    {
        var session = CreateAwaitingInitialBooks();
        var readinessRepository = new StubCollectorTokenReadinessRepository();
        var handler = CreateHandler(
            new StubCollectorSessionRepository(session),
            readinessRepository);

        var result = await handler.RecordInitialBookEnqueuedAsync(
            session.Id,
            TokenId.Create("1001").Value,
            1,
            default,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        readinessRepository.Recorded.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordInitialBookEnqueuedAsync_WhenPersistenceFails_ShouldPropagateException()
    {
        var session = CreateAwaitingInitialBooks();
        var readinessRepository = new StubCollectorTokenReadinessRepository
        {
            RecordException = new InvalidOperationException("Persistence failed.")
        };
        var handler = CreateHandler(
            new StubCollectorSessionRepository(session),
            readinessRepository);

        Func<Task> record = () => handler.RecordInitialBookEnqueuedAsync(
            session.Id,
            TokenId.Create("1001").Value,
            1,
            EnqueuedAt,
            CancellationToken.None);

        await record.Should().ThrowAsync<InvalidOperationException>();
    }

    private static CollectorSessionAggregate CreateAwaitingInitialBooks()
    {
        var session = CollectorSessionTestFactory.CreateScheduled(createdAt: CreatedAt);
        session.BeginPreparation(CreatedAt.AddSeconds(1));
        session.MarkAwaitingInitialBooks();
        return session;
    }

    private static CollectorRuntimeReadinessHandler CreateHandler(
        ICollectorSessionRepository sessionRepository,
        ICollectorTokenReadinessRepository readinessRepository) => new(
            sessionRepository,
            readinessRepository,
            new CollectorSessionInvalidationCoordinator(
                sessionRepository,
                new StubRuntime()),
            TimeProvider.System);

    private sealed class StubCollectorTokenReadinessRepository
        : ICollectorTokenReadinessRepository
    {
        public List<CollectorTokenReadiness> Recorded { get; } = [];
        public Exception? RecordException { get; init; }

        public Task RecordInitialBookEnqueuedAsync(
            CollectorTokenReadiness readiness,
            CancellationToken cancellationToken)
        {
            if (RecordException is not null)
                return Task.FromException(RecordException);

            Recorded.Add(readiness);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<CollectorTokenReadiness>> GetAsync(
            CollectorSessionId sessionId,
            long connectionEpoch,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubCollectorSessionRepository(
        params CollectorSessionAggregate[] sessions)
        : ICollectorSessionRepository
    {
        private readonly Queue<CollectorSessionAggregate> _sessions = new(sessions);

        public Task<CollectorSessionAggregate?> GetByIdAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<CollectorSessionAggregate?>(
                _sessions.TryDequeue(out var session) ? session : null);
        }

        public Task<CollectorSessionAggregate?> GetExclusiveAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CollectorSessionAggregate?> GetActiveByMarketIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CollectorSessionAggregate?> GetCurrentByMarketIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyCollection<CollectorSessionAggregate>> GetActiveAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<CollectorSessionInsertStatus, Error>> TryAddAsync(
            CollectorSessionAggregate session,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<CollectorSessionUpdateStatus, Error>> TryUpdateAsync(
            CollectorSessionAggregate session,
            CollectorSessionStatus expectedStatus,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubRuntime : ICollectorRuntime
    {
        public void FenceSession(CollectorSessionId sessionId)
        {
        }

        public Task<UnitResult<Error>> StartAsync(
            CollectorRuntimeStartRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UnitResult<Error>> StopAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
