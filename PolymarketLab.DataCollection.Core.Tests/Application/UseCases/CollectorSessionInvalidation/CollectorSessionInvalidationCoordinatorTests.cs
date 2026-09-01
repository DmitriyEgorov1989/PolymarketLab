using CSharpFunctionalExtensions;
using FluentAssertions;
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

namespace PolymarketLab.DataCollection.Core.Tests.Application.UseCases.CollectorSessionInvalidation;

public sealed class CollectorSessionInvalidationCoordinatorTests
{
    private static readonly DateTimeOffset InvalidatingAt =
        DateTimeOffset.Parse("2026-09-01T10:00:01Z");
    private static readonly Error Failure = new(
        "collector.runtime.receive.failed",
        "WebSocket receive failed.",
        ErrorType.Failure);

    [Fact]
    public async Task InvalidateAsync_ShouldFenceRuntimeBeforePersistingDiagnostic()
    {
        var session = CollectorSessionTestFactory.CreateRunning(
            createdAt: InvalidatingAt.AddSeconds(-1));
        var calls = new List<string>();
        var repository = new StubRepository(session, calls);
        var runtime = new StubRuntime(calls);
        var coordinator = new CollectorSessionInvalidationCoordinator(
            repository,
            runtime);

        var result = await coordinator.InvalidateAsync(
            session.Id,
            InvalidatingAt,
            CollectorStopReason.FatalWebSocketError,
            Failure,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(session);
        calls.Should().Equal("runtime.fence", "repository.update");
        session.Status.Should().Be(CollectorSessionStatus.Invalidating);
        session.InvalidatingAt.Should().Be(InvalidatingAt);
        session.FailureCode.Should().Be(Failure.Code);
        session.FailureMessage.Should().Be(Failure.Message);
    }

    [Fact]
    public async Task InvalidateAsync_WhenAlreadyInvalidating_ShouldPreserveFirstFailure()
    {
        var session = CollectorSessionTestFactory.CreateScheduled(
            createdAt: InvalidatingAt.AddSeconds(-1));
        session.BeginInvalidation(
            InvalidatingAt,
            CollectorStopReason.Requested,
            "collector.stop.requested",
            "Collector stop was requested before successful completion.");
        var repository = new StubRepository(session, []);
        var coordinator = new CollectorSessionInvalidationCoordinator(
            repository,
            new StubRuntime([]));

        var result = await coordinator.InvalidateAsync(
            session.Id,
            InvalidatingAt.AddSeconds(1),
            CollectorStopReason.ApplicationShutdown,
            Failure,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repository.UpdateCount.Should().Be(0);
        session.StopReason.Should().Be(CollectorStopReason.Requested);
        session.FailureCode.Should().Be("collector.stop.requested");
    }

    private sealed class StubRuntime(List<string> calls) : ICollectorRuntime
    {
        public void FenceSession(CollectorSessionId sessionId) =>
            calls.Add("runtime.fence");

        public Task<UnitResult<Error>> StartAsync(
            CollectorRuntimeStartRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UnitResult<Error>> StopAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubRepository(
        CollectorSessionAggregate session,
        List<string> calls) : ICollectorSessionRepository
    {
        public int UpdateCount { get; private set; }

        public Task<CollectorSessionAggregate?> GetByIdAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken) => Task.FromResult<CollectorSessionAggregate?>(session);

        public Task<Result<CollectorSessionUpdateStatus, Error>> TryUpdateAsync(
            CollectorSessionAggregate current,
            CollectorSessionStatus expectedStatus,
            CancellationToken cancellationToken)
        {
            UpdateCount++;
            calls.Add("repository.update");
            return Task.FromResult(Result.Success<CollectorSessionUpdateStatus, Error>(
                CollectorSessionUpdateStatus.Updated));
        }

        public Task<CollectorSessionAggregate?> GetExclusiveAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CollectorSessionAggregate?> GetActiveByMarketIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CollectorSessionAggregate?> GetCurrentByMarketIdAsync(
            MarketId marketId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyCollection<CollectorSessionAggregate>> GetActiveAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<CollectorSessionInsertStatus, Error>> TryAddAsync(
            CollectorSessionAggregate current,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
