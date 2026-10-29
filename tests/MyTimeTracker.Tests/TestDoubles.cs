using MyTimeTracker.Core.Abstractions;

namespace MyTimeTracker.Tests;

internal sealed class FakeWallClock : IWallClock
{
    public FakeWallClock(DateTimeOffset now, TimeZoneInfo? timeZone = null)
    {
        UtcNow = now;
        LocalTimeZone = timeZone ?? TimeZoneInfo.Utc;
    }

    public DateTimeOffset UtcNow { get; private set; }
    public TimeZoneInfo LocalTimeZone { get; }

    public void Set(DateTimeOffset value) => UtcNow = value;
    public void Advance(TimeSpan delta) => UtcNow += delta;
}

internal sealed class FakeMonotonicClock : IMonotonicClock
{
    public TimeSpan Elapsed { get; private set; }
    public void Advance(TimeSpan delta) => Elapsed += delta;
}

internal sealed class FakeIdleTimeProvider : IIdleTimeProvider
{
    public TimeSpan Idle { get; private set; }
    public TimeSpan GetIdleTime() => Idle;
    public void Advance(TimeSpan delta) => Idle += delta;
    public void Input() => Idle = TimeSpan.Zero;
    public void Set(TimeSpan value) => Idle = value;
}

internal sealed class TestClockSet
{
    public TestClockSet(DateTimeOffset now, TimeZoneInfo? timeZone = null)
    {
        Wall = new FakeWallClock(now, timeZone);
        Monotonic = new FakeMonotonicClock();
        Idle = new FakeIdleTimeProvider();
    }

    public FakeWallClock Wall { get; }
    public FakeMonotonicClock Monotonic { get; }
    public FakeIdleTimeProvider Idle { get; }

    /// <summary>Advance all clocks assuming no keyboard/mouse input.</summary>
    public void AdvanceIdle(TimeSpan delta)
    {
        Wall.Advance(delta);
        Monotonic.Advance(delta);
        Idle.Advance(delta);
    }

    /// <summary>Advance while the user remains active (idle stays zero).</summary>
    public void AdvanceActive(TimeSpan delta)
    {
        Wall.Advance(delta);
        Monotonic.Advance(delta);
        Idle.Input();
    }

    public void ReturnFromIdle(TimeSpan delta = default)
    {
        Idle.Input();
        if (delta > TimeSpan.Zero)
        {
            Wall.Advance(delta);
            Monotonic.Advance(delta);
        }
    }

    public void AdvanceTimeOnly(TimeSpan delta)
    {
        Wall.Advance(delta);
        Monotonic.Advance(delta);
    }
}
