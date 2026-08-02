using FluentAssertions;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Tests.Domain.Models.CollectorSession;

public sealed class CollectorSessionTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(CollectorSessionStatus.Starting)]
    [InlineData(CollectorSessionStatus.Running)]
    [InlineData(CollectorSessionStatus.Stopping)]
    public void Interrupt_WithActiveSession_ShouldSetInterruptedState(
        CollectorSessionStatus initialStatus)
    {
        var session = CreateSession(initialStatus);
        var interruptedAt = CreatedAt.AddMinutes(1);

        var result = session.Interrupt(
            interruptedAt,
            CollectorStopReason.ProcessTerminated);

        result.IsSuccess.Should().BeTrue();
        session.Status.Should().Be(CollectorSessionStatus.Interrupted);
        session.StoppedAt.Should().Be(interruptedAt);
        session.StopReason.Should().Be(CollectorStopReason.ProcessTerminated);
        session.FailureCode.Should().BeNull();
        session.FailureMessage.Should().BeNull();
    }

    [Fact]
    public void MarkStopping_WithRunningSession_ShouldSetStoppingState()
    {
        var session = CreateSession(CollectorSessionStatus.Running);

        var result = session.MarkStopping();

        result.IsSuccess.Should().BeTrue();
        session.Status.Should().Be(CollectorSessionStatus.Stopping);
    }

    [Fact]
    public void Interrupt_WithTimeBeforeStart_ShouldReturnError()
    {
        var session = CreateSession(CollectorSessionStatus.Running);

        var result = session.Interrupt(
            CreatedAt,
            CollectorStopReason.ProcessTerminated);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.session.stopped_at.invalid");
        session.Status.Should().Be(CollectorSessionStatus.Running);
    }

    [Fact]
    public void Interrupt_WithTerminalSession_ShouldReturnError()
    {
        var session = CreateSession(CollectorSessionStatus.Running);
        session.Stop(CreatedAt.AddMinutes(1), CollectorStopReason.Requested);

        var result = session.Interrupt(
            CreatedAt.AddMinutes(2),
            CollectorStopReason.ProcessTerminated);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.session.not_active");
        session.Status.Should().Be(CollectorSessionStatus.Stopped);
    }

    private static CollectorSessionAggregate CreateSession(
        CollectorSessionStatus status)
    {
        var session = CollectorSessionAggregate.Create(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            MarketId.Create(Guid.NewGuid()).Value,
            CreatedAt).Value;

        if (status is CollectorSessionStatus.Running or CollectorSessionStatus.Stopping)
            session.MarkRunning(CreatedAt.AddSeconds(1));

        if (status == CollectorSessionStatus.Stopping)
            session.MarkStopping();

        return session;
    }
}
