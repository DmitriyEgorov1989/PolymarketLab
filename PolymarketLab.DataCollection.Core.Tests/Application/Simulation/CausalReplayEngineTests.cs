using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection;
using PolymarketLab.DataCollection.Core.Application.Simulation.Replay;
using PolymarketLab.DataCollection.Core.Application.Simulation.Reporting;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.Simulation;

public sealed class CausalReplayEngineTests
{
    [Fact]
    public void AdvanceTo_SourceInversion_ShouldBlockWithoutUsingFutureSnapshot()
    {
        var at = SimulationFixture.Start;
        var journal = new SimulationJournal();
        var engine = new CausalReplayEngine(SimulationFixture.Dataset(SimulationFixture.Initial(),
            SimulationFixture.Message(2, at, 1, SimulationFixture.Change(2, "up", at, 0.48m, 5, source: 3000)),
            SimulationFixture.Message(3, at.AddSeconds(1), 1, SimulationFixture.Change(3, "up", at.AddSeconds(1), 0.48m, 7, source: 2000)),
            SimulationFixture.Message(4, at.AddSeconds(2), 1, SimulationFixture.Book(4, 0, "up", at.AddSeconds(2), source: 4000))),
            new OrderBookProjector(), journal);
        var bad = engine.AdvanceTo(at.AddSeconds(1)).Value;
        bad.BooksReady.Should().BeFalse();
        bad.Books[0].Asks[0].Size.Should().Be(5);
        bad.Books[0].Issue!.DetectedAt.Should().Be(at.AddSeconds(1));
        journal.Entries.Should().Contain(e => e.Kind == "BookRejected");
        engine.AdvanceTo(at.AddSeconds(2)).Value.BooksReady.Should().BeTrue();
    }

    [Fact]
    public void AdvanceTo_DeltaBeforeNewEpochBook_ShouldNotApplyToOldState()
    {
        var at = SimulationFixture.Start;
        var journal = new SimulationJournal();
        var engine = new CausalReplayEngine(SimulationFixture.Dataset(SimulationFixture.Initial(),
            SimulationFixture.Message(2, at, 2, SimulationFixture.Change(2, "up", at, 0.48m, 7, 2))),
            new OrderBookProjector(), journal);
        var observation = engine.AdvanceTo(at).Value;
        observation.BooksReady.Should().BeFalse();
        observation.Books.Should().OnlyContain(b => !b.HasSnapshot && b.Asks.IsEmpty);
        journal.Entries.Should().Contain(e => e.Kind == "MissingSnapshot");
    }

    [Fact]
    public void AdvanceTo_ShouldExposeOnlyAvailableEventsAndOwnObservations()
    {
        var at = SimulationFixture.Start.AddMilliseconds(500);
        var dataset = SimulationFixture.Dataset(SimulationFixture.Initial(),
            SimulationFixture.Message(2, at, 1, SimulationFixture.Change(2, "up", at, 0.48m, 0)));
        var engine = new CausalReplayEngine(dataset, new OrderBookProjector(), new SimulationJournal());
        var before = engine.AdvanceTo(SimulationFixture.Start).Value;
        before.Books[0].Asks.Should().ContainSingle();
        var after = engine.AdvanceTo(at).Value;
        after.Books[0].Asks.Should().BeEmpty();
        before.Books[0].Asks.Should().ContainSingle();
        engine.AdvanceTo(SimulationFixture.Start).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void AdvanceTo_NewEpochWithoutBothBooks_ShouldNotReuseLevelsOrTick()
    {
        var at = SimulationFixture.Start.AddSeconds(1);
        var engine = new CausalReplayEngine(SimulationFixture.Dataset(SimulationFixture.Initial(),
            SimulationFixture.Message(2, at, 2, SimulationFixture.Book(2, 0, "up", at, 2, tick: null))),
            new OrderBookProjector(), new SimulationJournal());
        var result = engine.AdvanceTo(at).Value;
        result.BooksReady.Should().BeFalse();
        result.Books[0].TickSize.Should().BeNull();
        result.Books[1].Asks.Should().BeEmpty();
    }

    [Fact]
    public void AdvanceTo_ClockRegressionAndEmptyRoot_ShouldPreserveArchiveCursor()
    {
        var at = SimulationFixture.Start;
        var journal = new SimulationJournal();
        var engine = new CausalReplayEngine(SimulationFixture.Dataset(SimulationFixture.Initial(),
            SimulationFixture.Message(2, at), SimulationFixture.Message(3, at.AddTicks(-9820))),
            new OrderBookProjector(), journal);
        engine.AdvanceTo(at).Value.RawMessageId.Should().Be(3);
        journal.Entries.Should().Contain(e => e.Kind == "ClockAdjusted");
    }

    [Fact]
    public void AdvanceTo_FutureSuffixAndDifferentReadSteps_ShouldNotChangePrefix()
    {
        var firstJournal = new SimulationJournal();
        var first = new CausalReplayEngine(SimulationFixture.Dataset(), new OrderBookProjector(), firstJournal);
        var at = SimulationFixture.Start;
        var otherJournal = new SimulationJournal();
        var other = new CausalReplayEngine(SimulationFixture.Dataset(SimulationFixture.Initial(),
            SimulationFixture.Message(2, at.AddSeconds(1))), new OrderBookProjector(), otherJournal);
        first.AdvanceTo(at.AddMilliseconds(-500));
        first.AdvanceTo(at).Value.Should().BeEquivalentTo(other.AdvanceTo(at).Value);
        firstJournal.Entries.Should().Equal(otherJournal.Entries);
    }
}
