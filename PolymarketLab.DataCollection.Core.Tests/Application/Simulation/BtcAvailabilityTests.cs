using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.Simulation.ExternalData;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.Simulation;

public sealed class BtcAvailabilityTests
{
    [Fact]
    public void Observe_ShouldNotInferAvailabilityFromSourceTime()
    {
        var at = SimulationFixture.Start;
        var point = new BtcObservation(1, "synthetic", "BTC/USD", at, at.AddSeconds(1), 100000);
        BtcAvailabilityTimeline.Observe([point], at, TimeSpan.FromSeconds(2)).Status.Should().Be(BtcDataStatus.Missing);
        BtcAvailabilityTimeline.Observe([point], at.AddSeconds(1), TimeSpan.FromSeconds(2)).Status.Should().Be(BtcDataStatus.Fresh);
        BtcAvailabilityTimeline.Observe([point], at.AddSeconds(2), TimeSpan.FromSeconds(2)).Status.Should().Be(BtcDataStatus.Stale);
    }

    [Fact]
    public void Observe_UnknownSourceTimeAndExplicitGap_ShouldRemainUnknown()
    {
        var at = SimulationFixture.Start;
        BtcAvailabilityTimeline.Observe([new(1, "synthetic", "BTC/USD", null, at, 100000)], at,
            TimeSpan.FromSeconds(1)).Status.Should().Be(BtcDataStatus.UnknownSourceTime);
        BtcAvailabilityTimeline.Observe([new(1, "synthetic", "BTC/USD", null, at, null, true)], at,
            TimeSpan.FromSeconds(1)).Status.Should().Be(BtcDataStatus.Gap);
    }
}
