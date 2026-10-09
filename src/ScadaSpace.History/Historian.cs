using System.Globalization;
using System.Text;
using ScadaSpace.Core;
using ScadaSpace.Tags;

namespace ScadaSpace.History;

public enum AggregateKind { Average, Minimum, Maximum, Count, Last }
public sealed record HistoryBucket(DateTimeOffset Start, DateTimeOffset End, Scalar Value, Quality Quality, int Count);

/// <summary>A bounded historian with explicit retention. Bucket averages are sample-weighted, not time-weighted.</summary>
public sealed class Historian : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Ring> _series = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TagDefinition> _definitions;
    private readonly IDisposable _subscription;
    private readonly int _capacity;
    private sealed class Ring(int capacity)
    {
        private readonly QualifiedValue[] _items = new QualifiedValue[capacity];
        private int _head;
        private int _count;
        public QualifiedValue? Last => _count == 0 ? null : _items[(_head + _count - 1) % capacity];
        public void Add(QualifiedValue value)
        {
            if (_count < capacity) { _items[(_head + _count) % capacity] = value; _count++; }
            else { _items[_head] = value; _head = (_head + 1) % capacity; }
        }
        public QualifiedValue[] Copy()
        {
            var result = new QualifiedValue[_count];
            for (var i = 0; i < _count; i++) result[i] = _items[(_head + i) % capacity];
            return result;
        }
    }
    public Historian(ITagProvider tags, int capacityPerTag = 4096)
    {
        if (capacityPerTag is < 2 or > 1000000) throw new ArgumentOutOfRangeException(nameof(capacityPerTag));
        _capacity = capacityPerTag;
        _definitions = tags.Browse().Where(x => x.HistoryEnabled).ToDictionary(x => x.Path, StringComparer.Ordinal);
        foreach (var path in _definitions.Keys) Append(path, tags.Read(path));
        _subscription = tags.Subscribe(change => Append(change.Path, change.Current));
    }
    public bool Append(string path, QualifiedValue value)
    {
        lock (_gate)
        {
            if (!_definitions.TryGetValue(path, out var definition)) return false;
            if (!_series.TryGetValue(path, out var ring)) _series[path] = ring = new(_capacity);
            if (ring.Last is { } last)
            {
                // UTC timestamps can repeat, but out-of-order insertion is rejected rather than corrupting charts.
                if (value.Timestamp < last.Timestamp || (value.Timestamp == last.Timestamp && value.Revision <= last.Revision)) return false;
                if (definition.HistoryDeadband > 0 && last.Quality == value.Quality && value.Value.Kind == ScalarKind.Number && Math.Abs(last.Value.AsNumber() - value.Value.Number) < definition.HistoryDeadband) return false;
            }
            ring.Add(value);
            return true;
        }
    }
    public QualifiedValue[] Query(string path, DateTimeOffset? start = null, DateTimeOffset? end = null)
    {
        if (start > end) throw new ArgumentException("Start must not follow end.");
        lock (_gate)
            return _series.TryGetValue(path, out var ring)
                ? ring.Copy().Where(x => (!start.HasValue || x.Timestamp >= start.Value) && (!end.HasValue || x.Timestamp <= end.Value)).ToArray()
                : [];
    }
    public HistoryBucket[] Aggregate(string path, DateTimeOffset start, DateTimeOffset end, int bucketCount, AggregateKind kind)
    {
        if (bucketCount is < 1 or > 10000 || end <= start) throw new ArgumentException("Invalid aggregation interval.");
        var points = Query(path, start, end);
        var ticks = (end - start).Ticks;
        var groups = Enumerable.Range(0, bucketCount).Select(_ => new List<QualifiedValue>()).ToArray();
        foreach (var point in points)
        {
            var index = Math.Min(bucketCount - 1, (int)((point.Timestamp - start).Ticks / (double)ticks * bucketCount));
            groups[index].Add(point);
        }
        return groups.Select((group, index) =>
        {
            var good = group.Where(p => p.IsGood).ToArray();
            var numbers = good.Where(p => p.Value.Kind == ScalarKind.Number).Select(p => p.Value.Number).ToArray();
            var value = kind switch
            {
                AggregateKind.Count => Scalar.From(good.Length),
                AggregateKind.Last when good.Length > 0 => good[^1].Value,
                AggregateKind.Average when numbers.Length > 0 => Scalar.From(numbers.Average()),
                AggregateKind.Minimum when numbers.Length > 0 => Scalar.From(numbers.Min()),
                AggregateKind.Maximum when numbers.Length > 0 => Scalar.From(numbers.Max()),
                _ => Scalar.Null
            };
            return new HistoryBucket(start.AddTicks((long)(ticks * (index / (double)bucketCount))), start.AddTicks((long)(ticks * ((index + 1) / (double)bucketCount))), value, value.Kind == ScalarKind.Null ? Quality.BadNotFound : good.Length == group.Count ? Quality.Good : Quality.Uncertain, good.Length);
        }).ToArray();
    }
    /// <summary>Preserves per-pixel min/max excursions instead of averaging away short alarm spikes.</summary>
    public static QualifiedValue[] Decimate(IReadOnlyList<QualifiedValue> points, int pixels)
    {
        if (pixels < 1) throw new ArgumentOutOfRangeException(nameof(pixels));
        if (points.Count <= pixels * 2) return points.ToArray();
        var result = new List<QualifiedValue>(pixels * 2 + 2) { points[0] };
        for (var i = 0; i < pixels; i++)
        {
            var from = i * points.Count / pixels;
            var to = (i + 1) * points.Count / pixels;
            var slice = points.Skip(from).Take(to - from).ToArray();
            if (slice.Any(p => !p.IsGood || p.Value.Kind != ScalarKind.Number)) { result.Add(slice[0]); continue; }
            var min = slice.MinBy(x => x.Value.Number);
            var max = slice.MaxBy(x => x.Value.Number);
            if (min.Timestamp <= max.Timestamp) { result.Add(min); result.Add(max); }
            else { result.Add(max); result.Add(min); }
        }
        result.Add(points[^1]);
        return result.Distinct().OrderBy(x => x.Timestamp).ThenBy(x => x.Revision).ToArray();
    }
    public string ExportCsv(string path, DateTimeOffset? start = null, DateTimeOffset? end = null)
    {
        static string Cell(string s) => "\"" + (s.Length > 0 && "=+-@\t\r".Contains(s[0]) ? "'" : "") + s.Replace("\"", "\"\"") + "\"";
        var text = new StringBuilder("timestamp,tag,value,quality,revision\r\n");
        foreach (var point in Query(path, start, end)) text.Append(point.Timestamp.ToString("O", CultureInfo.InvariantCulture)).Append(',').Append(Cell(path)).Append(',').Append(Cell(point.Value.ToString())).Append(',').Append(point.Quality).Append(',').Append(point.Revision).Append("\r\n");
        return text.ToString();
    }
    public void Dispose() => _subscription.Dispose();
}
