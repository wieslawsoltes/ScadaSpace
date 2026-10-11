namespace ScadaSpace.History;

public readonly record struct TrendRange(DateTimeOffset Start, DateTimeOffset End)
{
    public double Seconds => (End - Start).TotalSeconds;
}

/// <summary>Pure time-axis state for live, held, panned and cursor-anchored zoomed charts. Times are UTC.</summary>
public sealed class TrendViewport
{
    public const double MinimumSeconds = .001;
    public const double MaximumSeconds = 86400;
    private DateTimeOffset _latest = DateTimeOffset.UnixEpoch;
    private double _baseSeconds = 120;
    public bool IsLive { get; private set; } = true;
    public TrendRange Range { get; private set; } = new(DateTimeOffset.UnixEpoch.AddSeconds(-120), DateTimeOffset.UnixEpoch);
    public double BaseSeconds => _baseSeconds;
    public void Update(DateTimeOffset latest, double baseSeconds)
    {
        if (!double.IsFinite(baseSeconds) || baseSeconds < MinimumSeconds || baseSeconds > MaximumSeconds) throw new ArgumentOutOfRangeException(nameof(baseSeconds));
        var changed = baseSeconds != _baseSeconds; _latest = latest.ToUniversalTime(); _baseSeconds = baseSeconds;
        if (changed) IsLive = true;
        if (IsLive) Range = Make(_latest.UtcTicks - (decimal)_baseSeconds * TimeSpan.TicksPerSecond, (decimal)_baseSeconds * TimeSpan.TicksPerSecond);
    }
    public void Hold() => IsLive = false;
    public void Reset() { IsLive = true; Update(_latest, _baseSeconds); }
    public void Pan(double seconds)
    {
        if (!double.IsFinite(seconds) || Math.Abs(seconds) > MaximumSeconds * 100) throw new ArgumentOutOfRangeException(nameof(seconds));
        Range = Make(Range.Start.UtcTicks + (decimal)seconds * TimeSpan.TicksPerSecond, (Range.End - Range.Start).Ticks); IsLive = false;
    }
    public void Zoom(double factor, double anchor = .5)
    {
        if (!double.IsFinite(factor) || factor <= 0 || !double.IsFinite(anchor) || anchor < 0 || anchor > 1) throw new ArgumentOutOfRangeException(nameof(factor));
        var oldTicks = (Range.End - Range.Start).Ticks;
        var seconds = Math.Clamp(Range.Seconds * factor, MinimumSeconds, MaximumSeconds);
        var ticks = (decimal)seconds * TimeSpan.TicksPerSecond;
        Range = Make(Range.Start.UtcTicks + oldTicks * (decimal)anchor - ticks * (decimal)anchor, ticks); IsLive = false;
    }
    public void Select(DateTimeOffset start, DateTimeOffset end)
    {
        var seconds = (end - start).TotalSeconds;
        if (seconds < MinimumSeconds || seconds > MaximumSeconds) throw new ArgumentException("Selected range must span 1 ms to one day.");
        Range = new(start.ToUniversalTime(), end.ToUniversalTime()); IsLive = false;
    }
    public DateTimeOffset TimeAt(double fraction)
    {
        if (!double.IsFinite(fraction)) throw new ArgumentOutOfRangeException(nameof(fraction));
        return Range.Start.AddTicks((long)((Range.End - Range.Start).Ticks * (decimal)Math.Clamp(fraction, 0, 1)));
    }
    private static TrendRange Make(decimal start, decimal duration)
    {
        var ticks = (long)duration;
        var first = (long)Math.Clamp(start, 0, (decimal)DateTimeOffset.MaxValue.UtcTicks - ticks);
        return new(new DateTimeOffset(first, TimeSpan.Zero), new DateTimeOffset(first + ticks, TimeSpan.Zero));
    }
}
