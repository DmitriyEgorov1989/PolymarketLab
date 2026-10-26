using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection;
using PolymarketLab.DataCollection.Core.Application.Simulation.Execution;
using PolymarketLab.DataCollection.Core.Application.Simulation.Replay;
using PolymarketLab.DataCollection.Core.Application.Simulation.Reporting;
using PolymarketLab.DataCollection.Core.Application.Simulation.Rules;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.Simulation;

public sealed class ImmediateExecutionTests
{
    [Fact]
    public void Execute_BeforeArrival_ShouldKeepReservationUntilDue()
    {
        var (execution, replay, _) = Create();
        var at = SimulationFixture.Start;
        execution.Submit(new SimulatedOrder(1, "up", TradeSide.Buy, 1, 0.48m, TimeSpan.FromMilliseconds(200)),
            replay.AdvanceTo(at).Value, Rules());
        execution.Execute(1, replay.AdvanceTo(at.AddMilliseconds(199)).Value, Rules()).Error.Code
            .Should().Be("simulation.order.not_due");
        execution.Balance.ReservedCash.Should().Be(0.48m);
        execution.Execute(1, replay.AdvanceTo(at.AddMilliseconds(200)).Value, Rules()).Value.Fills.Should().HaveCount(1);
    }

    internal static TradingRulesProfile Rules(decimal feeBps = 0, decimal moneyQuantum = 0.000001m) =>
        TradingRulesProfile.CreateExperimental("test-v1", "synthetic, not historical Polymarket fees",
            SimulationFixture.Start.AddDays(-1), SimulationFixture.Start.AddDays(-1), 1, 1, moneyQuantum, feeBps).Value;

    internal static (ImmediateExecutionEngine Execution, CausalReplayEngine Replay, SimulationJournal Journal) Create()
    {
        var journal = new SimulationJournal();
        return (new ImmediateExecutionEngine(100m, ["up", "down"], journal),
            new CausalReplayEngine(SimulationFixture.Dataset(), new OrderBookProjector(), journal), journal);
    }

    [Fact]
    public void Execute_MultipleLevels_ShouldChargeDepthAndRoundedCumulativeFee()
    {
        var journal = new SimulationJournal();
        var at = SimulationFixture.Start;
        var initial = SimulationFixture.Message(1, at.AddSeconds(-1), 1,
            SimulationFixture.Book(1, 0, "up", at.AddSeconds(-1), size: 6),
            SimulationFixture.Book(1, 1, "down", at.AddSeconds(-1)));
        var replay = new CausalReplayEngine(SimulationFixture.Dataset(initial,
            SimulationFixture.Message(2, at, 1, SimulationFixture.Change(2, "up", at, 0.49m, 4))),
            new OrderBookProjector(), journal);
        var execution = new ImmediateExecutionEngine(100, ["up", "down"], journal);
        var observation = replay.AdvanceTo(at).Value;
        var order = new SimulatedOrder(1, "up", TradeSide.Buy, 10, 0.49m, TimeSpan.Zero);
        execution.Submit(order, observation, Rules(100, 0.0001m)).IsSuccess.Should().BeTrue();

        var result = execution.Execute(1, observation, Rules(100, 0.0001m)).Value;

        result.Fills.Select(f => f.Quantity).Should().Equal(6m, 4m);
        result.Fills.Sum(f => f.Gross).Should().Be(4.84m);
        execution.Balance.FeesPaid.Should().Be(0.0484m);
        execution.Balance.FreeCash.Should().Be(95.1116m);
        execution.Balance.ReservedCash.Should().Be(0);
    }

    [Fact]
    public void Execute_PartialAndRepeatedOrder_ShouldNotReuseLiquidityOrReserve()
    {
        var (execution, replay, _) = Create();
        var observation = replay.AdvanceTo(SimulationFixture.Start).Value;
        execution.Submit(new SimulatedOrder(1, "up", TradeSide.Buy, 15, 0.48m, TimeSpan.Zero), observation, Rules());
        execution.Balance.ReservedCash.Should().Be(7.2m);
        execution.Execute(1, observation, Rules()).Value.CancelledQuantity.Should().Be(5);
        execution.Balance.FreeCash.Should().Be(95.2m);
        execution.Submit(new SimulatedOrder(2, "up", TradeSide.Buy, 10, 0.48m, TimeSpan.Zero), observation, Rules());
        execution.Execute(2, observation, Rules()).Value.Fills.Should().BeEmpty();
        execution.Balance.Positions[0].Quantity.Should().Be(10);
        execution.Balance.ReservedCash.Should().Be(0);
        execution.Execute(1, observation, Rules()).IsFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData("up", TradeSide.Sell, 1, 0.40, "simulation.order.funds")]
    [InlineData("down", TradeSide.Buy, 1000, 0.48, "simulation.order.funds")]
    [InlineData("up", TradeSide.Buy, 1, 0.485, "simulation.order.validation")]
    [InlineData("up", TradeSide.Buy, 0.5, 0.48, "simulation.order.validation")]
    public void Submit_InvalidOrder_ShouldNotChangeBalances(string asset, TradeSide side, double quantity, double price, string error)
    {
        var (execution, replay, _) = Create();
        var result = execution.Submit(new SimulatedOrder(1, asset, side, (decimal)quantity, (decimal)price, TimeSpan.Zero),
            replay.AdvanceTo(SimulationFixture.Start).Value, Rules());
        result.Error.Code.Should().Be(error);
        execution.Balance.FreeCash.Should().Be(100);
        execution.Balance.ReservedCash.Should().Be(0);
    }

    [Fact]
    public void Execute_SellOwnedTokens_ShouldUseBidsAndRealizeCostBasis()
    {
        var (execution, replay, _) = Create();
        var observation = replay.AdvanceTo(SimulationFixture.Start).Value;
        execution.Submit(new SimulatedOrder(1, "down", TradeSide.Buy, 5, 0.48m, TimeSpan.Zero), observation, Rules());
        execution.Execute(1, observation, Rules());
        execution.Submit(new SimulatedOrder(2, "down", TradeSide.Sell, 5, 0.40m, TimeSpan.Zero), observation, Rules());
        execution.Execute(2, observation, Rules());
        execution.Balance.FreeCash.Should().Be(99.6m);
        execution.Balance.RealizedPnl.Should().Be(-0.4m);
        execution.Balance.Positions[1].Should().Be(new PositionBalance("down", 0, 0, 0));
    }

    [Fact]
    public void Execute_ChangedBookDuringLatency_ShouldNotFillRemovedLevel()
    {
        var at = SimulationFixture.Start;
        var journal = new SimulationJournal();
        var replay = new CausalReplayEngine(SimulationFixture.Dataset(SimulationFixture.Initial(),
            SimulationFixture.Message(2, at.AddMilliseconds(100), 1,
                SimulationFixture.Change(2, "up", at.AddMilliseconds(100), 0.48m, 0))), new OrderBookProjector(), journal);
        var execution = new ImmediateExecutionEngine(100, ["up", "down"], journal);
        execution.Submit(new SimulatedOrder(1, "up", TradeSide.Buy, 10, 0.48m, TimeSpan.FromMilliseconds(200)),
            replay.AdvanceTo(at).Value, Rules());
        execution.Execute(1, replay.AdvanceTo(at.AddMilliseconds(200)).Value, Rules()).Value.Fills.Should().BeEmpty();
        execution.Balance.FreeCash.Should().Be(100);
    }

    [Fact]
    public void Submit_UnknownRules_ShouldNotTreatFeeAsZero()
    {
        var (execution, replay, _) = Create();
        execution.Submit(new SimulatedOrder(1, "up", TradeSide.Buy, 1, 0.48m, TimeSpan.Zero),
            replay.AdvanceTo(SimulationFixture.Start).Value, null).Error.Code.Should().Be("simulation.order.rules_unknown");
    }

    [Fact]
    public void Execute_FeeRoundedAcrossLevels_ShouldNotRoundEachFillIndependently()
    {
        var at = SimulationFixture.Start;
        var journal = new SimulationJournal();
        var initial = SimulationFixture.Message(1, at.AddSeconds(-1), 1,
            SimulationFixture.Book(1, 0, "up", at.AddSeconds(-1), size: 1),
            SimulationFixture.Book(1, 1, "down", at.AddSeconds(-1)));
        var replay = new CausalReplayEngine(SimulationFixture.Dataset(initial,
            SimulationFixture.Message(2, at, 1, SimulationFixture.Change(2, "up", at, 0.49m, 1))),
            new OrderBookProjector(), journal);
        var execution = new ImmediateExecutionEngine(100, ["up", "down"], journal);
        var observation = replay.AdvanceTo(at).Value;
        execution.Submit(new SimulatedOrder(1, "up", TradeSide.Buy, 2, 0.49m, TimeSpan.Zero), observation, Rules(100, 0.01m));
        var result = execution.Execute(1, observation, Rules(100, 0.01m)).Value;
        result.Fills.Select(f => f.Fee).Should().Equal(0.01m, 0m);
        execution.Balance.FreeCash.Should().Be(99.02m);
        execution.Balance.ReservedCash.Should().Be(0);
    }
}
