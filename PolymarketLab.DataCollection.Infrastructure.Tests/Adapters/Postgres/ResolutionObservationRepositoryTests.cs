using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PolymarketLab.DataCollection.Core.Application.Resolution;
using PolymarketLab.DataCollection.Core.Domain.Models.Resolution;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.Postgres;

public sealed class ResolutionObservationRepositoryTests
{
    [Fact]
    public async Task SaveWebSocketScanAsync_ShouldCheckpointCursorAndBeIdempotent()
    {
        await using var dbContext = CreateContext();
        var repository = new ResolutionObservationRepository(dbContext);
        var sessionId = CollectorSessionId.Create(Guid.NewGuid()).Value;
        var candidate = new WebSocketResolutionCandidate(
            11,
            2,
            4,
            new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
            "market-1",
            "condition-1",
            ["token-1", "token-2"],
            "token-1",
            "Yes");
        var scan = new DurableWebSocketResolutionScan(
            sessionId,
            11,
            [new DurableWebSocketResolutionValidation(
                candidate,
                DurableResolutionObservationStatus.Terminal,
                new ResolutionWinner("token-1", "Yes"),
                null,
                null)]);

        await repository.SaveWebSocketScanAsync(scan, CancellationToken.None);
        await repository.SaveWebSocketScanAsync(scan, CancellationToken.None);
        var state = await repository.GetStateAsync(sessionId, CancellationToken.None);

        state.LastScannedRawMessageId.Should().Be(11);
        state.Observations.Should().ContainSingle();
        var observation = state.Observations.Single();
        observation.RawMessageId.Should().Be(11);
        observation.RawItemIndex.Should().Be(2);
        observation.ConnectionEpoch.Should().Be(4);
        observation.Outcomes.Should().HaveCount(2);
        observation.Outcomes.Single(outcome => outcome.IsWinner).Outcome.Should().Be("Yes");
    }

    [Fact]
    public async Task GetStateAsync_ShouldReturnExternalObservationAndConfirmation()
    {
        await using var dbContext = CreateContext();
        var repository = new ResolutionObservationRepository(dbContext);
        var sessionId = CollectorSessionId.Create(Guid.NewGuid()).Value;
        var observedAt = new DateTimeOffset(2026, 9, 1, 12, 0, 2, TimeSpan.Zero);
        var observationId = await repository.SaveClobObservationAsync(
            sessionId,
            new ClobTerminalResolutionObservation(
                observedAt,
                "condition-1",
                true,
                false,
                ClobTerminalResolutionStatus.Terminal,
                [
                    new ClobResolutionOutcome("token-1", "Yes", 0, 1.00m),
                    new ClobResolutionOutcome("token-2", "No", 1, 0.00m)
                ],
                new ClobResolutionOutcome("token-1", "Yes", 0, 1.00m)),
            CancellationToken.None);
        await repository.RecordPollingCycleAsync(sessionId, observedAt, CancellationToken.None);
        await repository.SetConfirmationReferenceAsync(
            sessionId,
            new ResolutionConfirmationReference(observationId, observationId, observedAt),
            CancellationToken.None);

        var state = await repository.GetStateAsync(sessionId, CancellationToken.None);

        state.LastPollingCycleAt.Should().Be(observedAt);
        state.Confirmation.Should().Be(new ResolutionConfirmationReference(
            observationId,
            observationId,
            observedAt));
        state.Observations.Single().Should().BeEquivalentTo(new
        {
            Id = observationId,
            Source = ResolutionObservationSource.Clob,
            Status = DurableResolutionObservationStatus.Terminal,
            ConditionId = "condition-1",
            Closed = (bool?)true,
            AcceptingOrders = (bool?)false,
            Winner = new ResolutionWinner("token-1", "Yes")
        });
    }

    private static DataCollectionDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DataCollectionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new DataCollectionDbContext(options);
    }
}
