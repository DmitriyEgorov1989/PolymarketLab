using CSharpFunctionalExtensions;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.UseCases.Queries.GetCollectorSessionById;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.DataCollection.Core.Tests.TestSupport;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using Xunit;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Tests.Application.UseCases.Queries.GetCollectorSessionById;

public sealed class GetCollectorSessionByIdHandlerTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_WithFailedSession_ShouldReturnMappedFailure()
    {
        var session = CreateSession();
        CollectorSessionTestFactory.MarkRunning(session, CreatedAt.AddSeconds(1));
        session.Fail(
            CreatedAt.AddMinutes(1),
            CollectorStopReason.FatalWebSocketError,
            "collector.runtime.receive.failed",
            "Receive failed.");
        var lastMessageAt = CreatedAt.AddSeconds(30);
        var handler = CreateHandler(
            new StubRepository(session),
            new StubProgressRepository(new CollectorSessionProgress(
                session.Id,
                12,
                10,
                lastMessageAt,
                2)));

        var result = await handler.Handle(
            new GetCollectorSessionByIdQuery(session.Id.Value),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Session.Should().BeEquivalentTo(new
        {
            SessionId = session.Id.Value,
            MarketId = session.MarketId.Value,
            Status = "Failed",
            CreatedAt,
            StartedAt = (DateTimeOffset?)CreatedAt.AddSeconds(1),
            StoppedAt = (DateTimeOffset?)CreatedAt.AddMinutes(1),
            FailureCode = "collector.runtime.receive.failed",
            FailureMessage = "Receive failed.",
            MessagesReceived = 12L,
            MessagesPersisted = 10L,
            LastMessageAt = (DateTimeOffset?)lastMessageAt,
            ReconnectCount = 2L
        });
    }

    [Fact]
    public async Task Handle_WithMissingSession_ShouldReturnNotFound()
    {
        var sessionId = Guid.NewGuid();
        var handler = CreateHandler(new StubRepository());

        var result = await handler.Handle(
            new GetCollectorSessionByIdQuery(sessionId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Code.Should().Be("collector.query.session.not_found");
        result.Error.Single().Type.Should().Be(ErrorType.NotFound);
    }

    private static GetCollectorSessionByIdHandler CreateHandler(
        ICollectorSessionRepository repository,
        ICollectorSessionProgressRepository? progressRepository = null)
    {
        return new GetCollectorSessionByIdHandler(
            repository,
            progressRepository ?? new StubProgressRepository());
    }

    private static CollectorSessionAggregate CreateSession()
    {
        return CollectorSessionTestFactory.CreateScheduled(createdAt: CreatedAt);
    }

    private sealed class StubRepository(CollectorSessionAggregate? session = null)
        : ICollectorSessionRepository
    {
        public Task<CollectorSessionAggregate?> GetByIdAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                session?.Id == sessionId ? session : null);
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
