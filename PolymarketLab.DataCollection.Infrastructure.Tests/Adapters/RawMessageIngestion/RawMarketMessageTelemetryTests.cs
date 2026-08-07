using FluentAssertions;
using PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.RawMessageIngestion;

public sealed class RawMarketMessageTelemetryTests
{
    [Fact]
    public void RecordReceivedComplete_ShouldIncrementAndKeepLatestTimestamp()
    {
        using var telemetry = new RawMarketMessageTelemetry();
        var sessionId = CollectorSessionId.Create(Guid.NewGuid()).Value;
        var latest = DateTimeOffset.Parse("2026-08-07T12:00:02Z");

        telemetry.RecordReceivedComplete(sessionId, latest);
        telemetry.RecordReceivedComplete(
            sessionId,
            DateTimeOffset.Parse("2026-08-07T12:00:01Z"));

        var snapshot = telemetry.GetSnapshot(sessionId);
        snapshot.ReceivedComplete.Should().Be(2);
        snapshot.LastMessageAt.Should().Be(latest);
    }

    [Fact]
    public void RecordReconnect_ShouldIncrementWithoutResettingMessageCounters()
    {
        using var telemetry = new RawMarketMessageTelemetry();
        var sessionId = CollectorSessionId.Create(Guid.NewGuid()).Value;
        var receivedAt = DateTimeOffset.Parse("2026-08-07T12:00:00Z");
        telemetry.RecordReceivedComplete(sessionId, receivedAt);

        telemetry.RecordReconnect(sessionId);
        telemetry.RecordReconnect(sessionId);

        var checkpoint = telemetry.GetCheckpoint(sessionId);
        checkpoint.MessagesReceived.Should().Be(1);
        checkpoint.LastMessageAt.Should().Be(receivedAt);
        checkpoint.ReconnectCount.Should().Be(2);
    }

    [Fact]
    public async Task WaitUntilPersistedAsync_ShouldCompleteAtTarget()
    {
        using var telemetry = new RawMarketMessageTelemetry();
        var sessionId = CollectorSessionId.Create(Guid.NewGuid()).Value;
        var wait = telemetry.WaitUntilPersistedAsync(sessionId, 2, CancellationToken.None);

        telemetry.RecordPersisted(sessionId, 1);
        wait.IsCompleted.Should().BeFalse();
        telemetry.RecordPersisted(sessionId, 1);

        await wait.WaitAsync(TimeSpan.FromSeconds(1));
    }
}
