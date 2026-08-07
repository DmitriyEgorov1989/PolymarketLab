using CSharpFunctionalExtensions;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.UseCases.Queries.GetCollectorSessionByMarket;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Tests.Application.UseCases.Queries.GetCollectorSessionByMarket;

public sealed class GetCollectorSessionByMarketHandlerTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_WithCurrentSession_ShouldReturnMappedSession()
    {
        var session = CreateSession();
        session.MarkRunning(CreatedAt.AddSeconds(1));
        var repository = new StubRepository(session);
        var handler = CreateHandler(
            repository,
            new StubProgressRepository(new CollectorSessionProgress(
                session.Id,
                5,
                4,
                CreatedAt.AddSeconds(10),
                1)));

        var result = await handler.Handle(
            new GetCollectorSessionByMarketQuery(session.MarketId.Value),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Session!.SessionId.Should().Be(session.Id.Value);
        result.Value.Session.MarketId.Should().Be(session.MarketId.Value);
        result.Value.Session.Status.Should().Be("Running");
        result.Value.Session.MessagesReceived.Should().Be(5);
        result.Value.Session.MessagesPersisted.Should().Be(4);
        result.Value.Session.LastMessageAt.Should().Be(CreatedAt.AddSeconds(10));
        result.Value.Session.ReconnectCount.Should().Be(1);
        repository.RequestedMarketId.Should().Be(session.MarketId);
    }

    [Fact]
    public async Task Handle_WithoutSessions_ShouldReturnSuccessfulEmptyResponse()
    {
        var handler = CreateHandler(new StubRepository());

        var result = await handler.Handle(
            new GetCollectorSessionByMarketQuery(Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Session.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithEmptyMarketId_ShouldNotCallRepository()
    {
        var repository = new StubRepository();
        var handler = CreateHandler(repository);

        var result = await handler.Handle(
            new GetCollectorSessionByMarketQuery(Guid.Empty),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Code.Should().Be("collector.query.market_id.required");
        repository.CallCount.Should().Be(0);
    }

    private static GetCollectorSessionByMarketHandler CreateHandler(
        ICollectorSessionRepository repository,
        ICollectorSessionProgressRepository? progressRepository = null)
    {
        return new GetCollectorSessionByMarketHandler(
            new GetCollectorSessionByMarketValidator(),
            repository,
            progressRepository ?? new StubProgressRepository());
    }

    private static CollectorSessionAggregate CreateSession()
    {
        return CollectorSessionAggregate.Create(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            MarketId.Create(Guid.NewGuid()).Value,
            CreatedAt).Value;
    }

    private sealed class StubRepository(CollectorSessionAggregate? session = null)
        : ICollectorSessionRepository
    {
        public int CallCount { get; private set; }
        public MarketId? RequestedMarketId { get; private set; }

        public Task<CollectorSessionAggregate?> GetCurrentByMarketIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            RequestedMarketId = marketId;
            return Task.FromResult(
                session?.MarketId == marketId ? session : null);
        }

        public Task<CollectorSessionAggregate?> GetByIdAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CollectorSessionAggregate?> GetActiveByMarketIdAsync(
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

    private sealed class StubProgressRepository(CollectorSessionProgress? progress = null)
        : ICollectorSessionProgressRepository
    {
        public Task<CollectorSessionProgress> GetAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(progress ?? CollectorSessionProgress.Empty(sessionId));
        }

        public Task CheckpointAsync(
            CollectorSessionProgressCheckpoint checkpoint,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
