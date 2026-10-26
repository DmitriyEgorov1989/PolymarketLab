using System.Collections.Immutable;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection;
using PolymarketLab.DataCollection.Core.Application.Simulation;
using PolymarketLab.DataCollection.Core.Application.Simulation.ExternalData;
using PolymarketLab.DataCollection.Core.Application.Simulation.RunExperiment;
using Xunit;
using Change = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models.PriceChangeRecord;
using Events = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models.NormalizedOrderBookEvent;

namespace PolymarketLab.DataCollection.Core.Tests.Application.Simulation;

public sealed class ExperimentRunnerTests
{
    internal static ExperimentDefinition Definition(params ScheduledOrder[] orders) => new(1, 100,
        SimulationFixture.Start.AddMinutes(5), orders.ToImmutableArray(), [ImmediateExecutionTests.Rules()],
        [], null, false, [], null);

    internal static ScheduledOrder Buy(long id = 1, string asset = "up", int milliseconds = 0, int latency = 0) =>
        new(SimulationFixture.Start.AddMilliseconds(milliseconds),
            new(id, asset, TradeSide.Buy, 5, 0.48m, TimeSpan.FromMilliseconds(latency)));

    [Fact]
    public void Run_Twice_ShouldProduceIdenticalJournalAndPreserveDataset()
    {
        var dataset = SimulationFixture.Dataset();
        var before = dataset.Manifest;
        var beforeEvents = SimulationJson.Hash(dataset.Messages.SelectMany(m => m.Events).Select(e => SimulationJson.Serialize(e, e.GetType())));
        var definition = Definition(Buy(), Buy(2, "down", 1));
        var runner = new ExperimentRunner(new OrderBookProjector());
        var first = runner.Run(dataset, definition).Value;
        var second = runner.Run(dataset, definition).Value;
        first.Journal.Should().Equal(second.Journal);
        SimulationJson.Serialize(first).Should().Be(SimulationJson.Serialize(second));
        dataset.Manifest.Should().Be(before);
        SimulationJson.Hash(dataset.Messages.SelectMany(m => m.Events).Select(e => SimulationJson.Serialize(e, e.GetType())))
            .Should().Be(beforeEvents);
        first.SettlementStatus.Should().Be("Unconfirmed");
        first.Balance.FreeCash.Should().Be(95.2m);
        first.Balance.Positions.Should().OnlyContain(p => p.Quantity == 5 && p.Reserved == 0);
        first.FinalBtc.Status.Should().Be(BtcDataStatus.Missing);
    }

    [Fact]
    public void Run_FutureBtc_ShouldNotBeVisibleAtEarlierDecision()
    {
        var at = SimulationFixture.Start.AddMilliseconds(500);
        var definition = Definition(Buy(), Buy(2, milliseconds: 500)) with
        {
            Btc = [new BtcObservation(1, "synthetic", "BTC/USD", at, at, 100000)],
            MaximumBtcAge = TimeSpan.FromSeconds(1), RequireFreshBtc = true
        };
        var result = new ExperimentRunner(new OrderBookProjector()).Run(SimulationFixture.Dataset(), definition).Value;
        result.Journal.Count(e => e.Kind == "Fill").Should().Be(1);
        result.Journal.First(e => e.Kind == "DecisionObservation").Data.Should().NotContain("100000");
        result.Balance.Positions[0].Quantity.Should().Be(5);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_BtcStaleOrGapDuringLatency_ShouldCancelAndReleaseFunds(bool gap)
    {
        var at = SimulationFixture.Start;
        var points = ImmutableArray.Create(new BtcObservation(1, "synthetic", "BTC/USD", at, at, 100000));
        if (gap) points = points.Add(new BtcObservation(2, "synthetic", "BTC/USD", null, at.AddMilliseconds(100), null, true));
        var definition = Definition(Buy(latency: 200)) with
        {
            Btc = points, MaximumBtcAge = TimeSpan.FromMilliseconds(150), RequireFreshBtc = true
        };
        var result = new ExperimentRunner(new OrderBookProjector()).Run(SimulationFixture.Dataset(), definition).Value;
        result.Journal.Should().NotContain(e => e.Kind == "Fill");
        result.Balance.FreeCash.Should().Be(100);
        result.Balance.ReservedCash.Should().Be(0);
        result.FinalBtc.Status.Should().Be(gap ? BtcDataStatus.Gap : BtcDataStatus.Stale);
    }

    [Fact]
    public void Run_LateConfirmedOutcome_ShouldPayOnlyAtExplicitEpisodeEnd()
    {
        var end = SimulationFixture.Start.AddMinutes(5);
        var definition = Definition(Buy()) with
        {
            EpisodeEndsAt = end.AddSeconds(3),
            SettlementPolicy = new SettlementPolicy(["source-a", "source-b"], 1m),
            Settlement = [Evidence("source-a", "up", end.AddSeconds(1)), Evidence("source-b", "up", end.AddSeconds(2))]
        };
        var result = new ExperimentRunner(new OrderBookProjector()).Run(SimulationFixture.Dataset(), definition).Value;
        result.SettlementObservedAt.Should().Be(end.AddSeconds(2));
        result.SettlementAppliedAt.Should().Be(end.AddSeconds(3));
        result.Balance.FreeCash.Should().Be(102.6m);
        result.Balance.RealizedPnl.Should().Be(2.6m);
        result.Balance.Positions.Should().OnlyContain(p => p.Quantity == 0);
    }

    [Fact]
    public void Run_ConflictingOutcomes_ShouldNotInventPayout()
    {
        var end = SimulationFixture.Start.AddMinutes(5);
        var definition = Definition(Buy()) with
        {
            EpisodeEndsAt = end.AddSeconds(2),
            SettlementPolicy = new SettlementPolicy(["a", "b"], 1m),
            Settlement = [Evidence("a", "up", end.AddSeconds(1)), Evidence("b", "down", end.AddSeconds(2))]
        };
        var result = new ExperimentRunner(new OrderBookProjector()).Run(SimulationFixture.Dataset(), definition).Value;
        result.SettlementStatus.Should().Be("Conflicting");
        result.SettlementAppliedAt.Should().BeNull();
        result.Balance.Positions[0].Quantity.Should().Be(5);
    }

    [Fact]
    public void Run_NoBidsAtEnd_ShouldLeaveOpenPositionWithoutArtificialSale()
    {
        var at = SimulationFixture.Start.AddSeconds(1);
        var dataset = SimulationFixture.Dataset(SimulationFixture.Initial(), SimulationFixture.Message(2, at, 1,
            new Events.PriceChanges([new Change(2, 0, 20, "up", 2000, TradeSide.Buy, 0.4m, 0, null, null, null, 0, 1, at)])));
        var result = new ExperimentRunner(new OrderBookProjector()).Run(dataset, Definition(Buy())).Value;
        result.Valuations[0].UnknownQuantity.Should().Be(5);
        result.Valuations[0].EstimatedPnl.Should().BeNull();
        result.Balance.Positions[0].Quantity.Should().Be(5);
        result.Journal.Count(e => e.Kind == "Fill").Should().Be(1);
    }

    [Fact]
    public void Run_OrderArrivingAtEnd_ShouldBeCancelled()
    {
        var end = SimulationFixture.Start.AddMinutes(5);
        var action = Buy() with { At = end.AddMilliseconds(-100), Order = Buy(latency: 100).Order };
        var result = new ExperimentRunner(new OrderBookProjector()).Run(SimulationFixture.Dataset(), Definition(action)).Value;
        result.Journal.Should().NotContain(e => e.Kind == "Fill");
        result.Balance.FreeCash.Should().Be(100);
        result.Balance.ReservedCash.Should().Be(0);
    }

    [Fact]
    public void Run_ChangedRulesDuringLatency_ShouldRejectInsufficientReservation()
    {
        var at = SimulationFixture.Start.AddMilliseconds(100);
        var changed = Core.Application.Simulation.Rules.TradingRulesProfile.CreateExperimental(
            "new-version", "synthetic updated fee", at, at, 1, 1, 0.000001m, 100).Value;
        var definition = Definition(Buy(latency: 200)) with { Rules = [ImmediateExecutionTests.Rules(), changed] };
        var result = new ExperimentRunner(new OrderBookProjector()).Run(SimulationFixture.Dataset(), definition).Value;
        result.Journal.Should().Contain(e => e.Kind == "OrderRejected" && e.Data.Contains("rules_changed"));
        result.Balance.FreeCash.Should().Be(100);
        result.Balance.ReservedCash.Should().Be(0);
    }

    [Fact]
    public void Run_ReconnectAndRepeatedSnapshot_ShouldNotRestoreConsumedAsk()
    {
        var at = SimulationFixture.Start.AddSeconds(1);
        var dataset = SimulationFixture.Dataset(SimulationFixture.Initial(), SimulationFixture.Message(2, at, 2,
            SimulationFixture.Book(2, 0, "up", at, 2), SimulationFixture.Book(2, 1, "down", at, 2)));
        var first = Buy() with { Order = Buy().Order with { Quantity = 10 } };
        var second = Buy(2, milliseconds: 1000);
        var result = new ExperimentRunner(new OrderBookProjector()).Run(dataset, Definition(first, second)).Value;
        result.Journal.Count(e => e.Kind == "Fill").Should().Be(1);
        result.Balance.Positions[0].Quantity.Should().Be(10);
        result.DataIssues.Should().Contain("ConnectionGap");
    }

    private static SettlementEvidence Evidence(string source, string winner, DateTimeOffset at) =>
        new(source, $"synthetic:{source}", SimulationFixture.SessionId, "condition", winner, at, at);
}
