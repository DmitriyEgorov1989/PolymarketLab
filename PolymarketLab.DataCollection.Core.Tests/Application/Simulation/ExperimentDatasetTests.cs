using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.Simulation;
using PolymarketLab.DataCollection.Core.Application.Simulation.Datasets;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.Simulation;

public sealed class ExperimentDatasetTests
{
    [Fact]
    public void Create_ShouldOwnSnapshotLevelsEvenIfCallerMutatesItsArray()
    {
        var at = SimulationFixture.Start.AddSeconds(-1);
        var levels = new[] { new Core.Application.Normalization.Models.BookLevelRecord(
            Core.Application.Normalization.Models.OrderBookSide.Ask, 0, 0.48m, 10) };
        var book = new Core.Application.OrderBooks.Projection.Models.NormalizedOrderBookEvent.BookSnapshot(
            new Core.Application.OrderBooks.Projection.Models.BookSnapshotRecord(1, 0, 10, "up", "condition", 1000, "hash", 0.01m,
                [new Core.Application.Normalization.Models.BookLevelRecord(Core.Application.Normalization.Models.OrderBookSide.Bid, 0, 0.4m, 10)],
                levels, 1, at));
        var dataset = SimulationFixture.Dataset(SimulationFixture.Message(1, at, 1,
            book, SimulationFixture.Book(1, 1, "down", at)));
        levels[0] = new Core.Application.Normalization.Models.BookLevelRecord(
            Core.Application.Normalization.Models.OrderBookSide.Ask, 0, 0.99m, 1);
        var engine = new Core.Application.Simulation.Replay.CausalReplayEngine(dataset,
            new Core.Application.OrderBooks.Projection.OrderBookProjector(),
            new Core.Application.Simulation.Reporting.SimulationJournal());
        engine.AdvanceTo(SimulationFixture.Start).Value.Books[0].Asks[0].Price.Should().Be(0.48m);
    }

    [Fact]
    public void Create_SameData_ShouldProduceSameManifest()
    {
        SimulationFixture.Dataset().Manifest.Should().Be(SimulationFixture.Dataset().Manifest);
    }

    [Fact]
    public void Create_EmptyRootAndClockRegression_ShouldPreserveAllHeaders()
    {
        var first = SimulationFixture.Initial();
        var empty = SimulationFixture.Message(2, first.ReceivedAt.AddTicks(-9820));
        var result = SimulationFixture.Dataset(first, empty);
        result.Messages.Should().HaveCount(2);
        result.Messages[1].Events.Should().BeEmpty();
        result.Manifest.Should().NotBe(SimulationFixture.Dataset().Manifest);
    }

    [Fact]
    public void Create_ChangedTypedEvent_ShouldChangeProjectionFingerprint()
    {
        var at = SimulationFixture.Start.AddSeconds(-1);
        var changed = SimulationFixture.Message(1, at, 1,
            SimulationFixture.Book(1, 0, "up", at, ask: 0.49m),
            SimulationFixture.Book(1, 1, "down", at));
        SimulationFixture.Dataset(changed).Manifest.ProjectionFingerprint.Should()
            .NotBe(SimulationFixture.Dataset().Manifest.ProjectionFingerprint);
    }

    [Fact]
    public void Create_DescendingRawIds_ShouldReturnExpectedError()
    {
        var result = ExperimentDataset.Create(SimulationFixture.SessionId, 1, "condition",
            SimulationFixture.Start, SimulationFixture.Start.AddMinutes(5),
            [new ReplayAsset("up", "Up", 0), new ReplayAsset("down", "Down", 1)],
            [SimulationFixture.Message(2, SimulationFixture.Start), SimulationFixture.Initial()]);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("simulation.dataset.order");
    }

    [Fact]
    public void CanonicalJson_ShouldNormalizeDecimalScaleAndUtcOffset()
    {
        SimulationJson.Hash(new { Price = 0.4800m, At = SimulationFixture.Start.ToOffset(TimeSpan.FromHours(3)) })
            .Should().Be(SimulationJson.Hash(new { Price = 0.48m, At = SimulationFixture.Start }));
    }
}
