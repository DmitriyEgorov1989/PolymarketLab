namespace PolymarketLab.DataCollection.Core.Application.Simulation.Replay;

/// <summary>Часы эксперимента без системных таймеров и обратного движения.</summary>
public sealed class ReplayTimeProvider(DateTimeOffset origin) : TimeProvider
{
    private readonly DateTimeOffset _origin = origin.ToUniversalTime();
    private DateTimeOffset _now = origin.ToUniversalTime();

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;
    /// <inheritdoc />
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    /// <inheritdoc />
    public override long GetTimestamp() => (_now - _origin).Ticks;

    internal void AdvanceTo(DateTimeOffset next)
    {
        if (next < _now)
            throw new InvalidOperationException("Replay scheduler moved backwards.");
        _now = next.ToUniversalTime();
    }

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        throw new NotSupportedException("Replay timers belong to the deterministic event queue.");
}
