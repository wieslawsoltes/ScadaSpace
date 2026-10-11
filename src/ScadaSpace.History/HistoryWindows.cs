using ScadaSpace.Core;

namespace ScadaSpace.History;

public readonly record struct HistoryStamp(int Count, long UtcTicks, long Revision);
public readonly record struct HistorySeriesInfo(HistoryStamp Stamp, DateTimeOffset? First, QualifiedValue? Last);
public sealed record HistoryWindow(DateTimeOffset Start, DateTimeOffset End, HistoryStamp Stamp, IReadOnlyList<QualifiedValue> Samples);
public sealed record HistoryPlotWindow(HistoryWindow Source, IReadOnlyList<QualifiedValue> Points);

/// <summary>One bounded, immutable plot window per path. UI-thread-affine; the historian snapshots themselves are atomic.</summary>
public sealed class HistoryWindowCache(Historian history, int capacity = 32)
{
    private sealed record Entry(DateTimeOffset Start, DateTimeOffset End, int Pixels, HistoryPlotWindow Window);
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();
    private readonly int _capacity = capacity is >= 1 and <= 256 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    public long Reads { get; private set; }
    public long Hits { get; private set; }
    public long SamplesCopied { get; private set; }
    public int Count => _entries.Count;
    public HistoryPlotWindow Get(string path, DateTimeOffset start, DateTimeOffset end, int pixels)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (start >= end || pixels is < 1 or > 16384) throw new ArgumentException("Invalid plot interval or pixel budget.");
        var info = history.GetSeriesInfo(path);
        if (_entries.TryGetValue(path, out var entry) && entry.Start == start && entry.End == end && entry.Pixels == pixels && entry.Window.Source.Stamp == info.Stamp)
        { Hits++; return entry.Window; }
        // ReadWindow captures stamp and samples under one lock. A concurrent write cannot
        // attach a new stamp to stale samples; a cache hit linearizes at GetSeriesInfo.
        var source = history.ReadWindow(path, start, end, includeBounds: true);
        var points = source.Samples.Count > pixels * 2 ? Array.AsReadOnly(Historian.Decimate(source.Samples, pixels)) : source.Samples;
        var window = new HistoryPlotWindow(source, points); Reads++; SamplesCopied += source.Samples.Count;
        if (!_entries.ContainsKey(path))
        {
            while (_entries.Count >= _capacity) _entries.Remove(_order.Dequeue());
            _order.Enqueue(path);
        }
        _entries[path] = new(start, end, pixels, window); return window;
    }
    public void Clear() { _entries.Clear(); _order.Clear(); }
}

public sealed partial class Historian
{
    private static HistoryStamp Stamp(Ring ring) => ring.Last is { } last ? new(ring.Count, last.Timestamp.UtcTicks, last.Revision) : default;
    public HistorySeriesInfo GetSeriesInfo(string path)
    {
        lock (_gate) return _series.TryGetValue(path, out var ring) ? new(Stamp(ring), ring.First?.Timestamp, ring.Last) : default;
    }
    /// <summary>Atomic owned snapshot. Optional adjacent samples support clipped continuity, not extrapolation.</summary>
    public HistoryWindow ReadWindow(string path, DateTimeOffset start, DateTimeOffset end, bool includeBounds = false, int maxSamples = 1000000)
    {
        if (maxSamples is < 1 or > 1000000) throw new ArgumentOutOfRangeException(nameof(maxSamples));
        if (start > end) throw new ArgumentException("Start must not follow end.");
        lock (_gate)
        {
            if (!_series.TryGetValue(path, out var ring)) return new(start, end, default, Array.Empty<QualifiedValue>());
            return new(start, end, Stamp(ring), Array.AsReadOnly(ring.Query(start, end, includeBounds, maxSamples)));
        }
    }
}

/// <summary>Binary-search X trace over ordered original samples. Never interpolates across bad quality or extrapolates.</summary>
public static class HistoryTrace
{
    public static QualifiedValue At(IReadOnlyList<QualifiedValue> samples, DateTimeOffset time)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var low = 0; var high = samples.Count;
        while (low < high) { var mid = low + (high - low) / 2; if (samples[mid].Timestamp <= time) low = mid + 1; else high = mid; }
        if (low == 0) return QualifiedValue.Bad(Quality.BadNotFound, time);
        var a = samples[low - 1];
        if (a.Timestamp == time) return a;
        if (low == samples.Count) return QualifiedValue.Bad(Quality.BadNotFound, time);
        var b = samples[low];
        if (!a.IsGood || !b.IsGood) return QualifiedValue.Bad(a.Quality > b.Quality ? a.Quality : b.Quality, time);
        if (a.Value.Kind != ScalarKind.Number || b.Value.Kind != ScalarKind.Number) return QualifiedValue.Bad(Quality.BadType, time);
        var fraction = (time - a.Timestamp).Ticks / (double)(b.Timestamp - a.Timestamp).Ticks;
        var value = a.Value.Number * (1 - fraction) + b.Value.Number * fraction;
        return double.IsFinite(value) ? new(Scalar.From(value), Quality.Good, time, Math.Max(a.Revision, b.Revision)) : QualifiedValue.Bad(Quality.BadType, time);
    }
}
