using System.Globalization;
using System.Text;
using ScadaSpace.Core;
using ScadaSpace.Tags;

namespace ScadaSpace.History;

public enum AggregateKind { Average, Minimum, Maximum, Count, Last }
public sealed record HistoryBucket(DateTimeOffset Start, DateTimeOffset End, Scalar Value, Quality Quality, int Count);

/// <summary>A bounded historian with explicit retention. Bucket averages are sample-weighted, not time-weighted.</summary>
public sealed partial class Historian : IDisposable
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
        public int Count => _count;
        public QualifiedValue? First => _count == 0 ? null : At(0);
        public QualifiedValue? Last => _count == 0 ? null : _items[(_head + _count - 1) % capacity];
        public void Add(QualifiedValue value)
        {
            if (_count < capacity) { _items[(_head + _count) % capacity] = value; _count++; }
            else { _items[_head] = value; _head = (_head + 1) % capacity; }
        }
        private QualifiedValue At(int index) => _items[(_head + index) % capacity];
        private int Bound(DateTimeOffset timestamp, bool upper)
        {
            var low = 0; var high = _count;
            while (low < high)
            {
                var mid = low + (high - low) / 2; var value = At(mid).Timestamp;
                if (value < timestamp || upper && value == timestamp) low = mid + 1;
                else high = mid;
            }
            return low;
        }
        public QualifiedValue[] Query(DateTimeOffset? start, DateTimeOffset? end, bool includeBounds = false)
        {
            var first = start.HasValue ? Bound(start.Value, false) : 0;
            var last = end.HasValue ? Bound(end.Value, true) : _count;
            if (includeBounds) { first = Math.Max(0, first - 1); last = Math.Min(_count, last + 1); }
            if (last <= first) return [];
            var result = new QualifiedValue[last - first];
            var offset = (_head + first) % capacity;
            var length = Math.Min(result.Length, capacity - offset);
            Array.Copy(_items, offset, result, 0, length);
            if (length < result.Length) Array.Copy(_items, 0, result, length, result.Length - length);
            return result;
        }
    }
    public Historian(ITagProvider tags, int capacityPerTag = 4096)
    {
        if (capacityPerTag is < 2 or > 1000000) throw new ArgumentOutOfRangeException(nameof(capacityPerTag));
        _capacity = capacityPerTag;
        _definitions = tags.Browse().Where(x => x.HistoryEnabled).ToDictionary(x => x.Path, StringComparer.Ordinal);
        _subscription = tags.Subscribe(change => Append(change.Path, change.Current));
        foreach (var path in _definitions.Keys) Append(path, tags.Read(path));
    }
    public bool Append(string path, QualifiedValue value)
    {
        lock (_gate)
        {
            if (!_definitions.TryGetValue(path, out var definition) || !value.Value.IsValid() || !Enum.IsDefined(value.Quality)) return false;
            if (!_series.TryGetValue(path, out var ring)) _series[path] = ring = new(_capacity);
            if (ring.Last is { } last)
            {
                if (value.Timestamp < last.Timestamp || (value.Timestamp == last.Timestamp && value.Revision <= last.Revision)) return false;
                if (definition.HistoryDeadband > 0 && last.Quality == value.Quality && value.Value.Kind == ScalarKind.Number && last.Value.Kind == ScalarKind.Number && Math.Abs(last.Value.Number - value.Value.Number) < definition.HistoryDeadband) return false;
            }
            ring.Add(value); return true;
        }
    }
    public QualifiedValue[] Query(string path, DateTimeOffset? start = null, DateTimeOffset? end = null)
    {
        if (start > end) throw new ArgumentException("Start must not follow end.");
        lock (_gate) return _series.TryGetValue(path, out var ring) ? ring.Query(start, end) : [];
    }
    public int Count(string path) { lock (_gate) return _series.TryGetValue(path, out var ring) ? ring.Count : 0; }
    public QualifiedValue? Latest(string path) { lock (_gate) return _series.TryGetValue(path, out var ring) ? ring.Last : null; }
    public HistoryBucket[] Aggregate(string path, DateTimeOffset start, DateTimeOffset end, int bucketCount, AggregateKind kind)
    {
        if (bucketCount is < 1 or > 10000 || end <= start || !Enum.IsDefined(kind)) throw new ArgumentException("Invalid aggregation interval or kind.");
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
            var quality = value.Kind == ScalarKind.Null ? Quality.BadNotFound : good.Length == group.Count ? Quality.Good : Quality.Uncertain;
            return new HistoryBucket(start.AddTicks((long)(ticks * (index / (double)bucketCount))), start.AddTicks((long)(ticks * ((index + 1) / (double)bucketCount))), value, quality, good.Length);
        }).ToArray();
    }
    /// <summary>Preserves extrema, endpoints and all quality transitions, which may exceed the nominal pixel budget.</summary>
    public static QualifiedValue[] Decimate(IReadOnlyList<QualifiedValue> points, int pixels)
    {
        if (pixels is < 1 or > 1000000) throw new ArgumentOutOfRangeException(nameof(pixels));
        if (points.Count <= pixels * 2) return points.ToArray();
        var selected = new SortedSet<int> { 0, points.Count - 1 };
        var minima = Enumerable.Repeat(-1, pixels).ToArray();
        var maxima = Enumerable.Repeat(-1, pixels).ToArray();
        var first = points[0].Timestamp;
        var duration = (points[^1].Timestamp - first).Ticks;
        bool Numeric(QualifiedValue sample) => sample.IsGood && sample.Value.Kind == ScalarKind.Number;
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            if (i > 0 && (point.Quality != points[i - 1].Quality || Numeric(point) != Numeric(points[i - 1])))
            { selected.Add(i - 1); selected.Add(i); }
            if (!Numeric(point)) continue;
            var bucket = duration > 0 ? Math.Clamp((int)((point.Timestamp - first).Ticks / (double)duration * pixels), 0, pixels - 1) : Math.Min(pixels - 1, (int)(i / (double)points.Count * pixels));
            if (minima[bucket] < 0 || point.Value.Number < points[minima[bucket]].Value.Number) minima[bucket] = i;
            if (maxima[bucket] < 0 || point.Value.Number > points[maxima[bucket]].Value.Number) maxima[bucket] = i;
        }
        foreach (var index in minima.Concat(maxima)) if (index >= 0) selected.Add(index);
        return selected.Select(index => points[index]).ToArray();
    }
    public string ExportCsv(string path, DateTimeOffset? start = null, DateTimeOffset? end = null)
    {
        static string Cell(string s, bool protect = true) => "\"" + (protect && s.TrimStart().Length > 0 && "=+-@".Contains(s.TrimStart()[0]) ? "'" : "") + s.Replace("\"", "\"\"") + "\"";
        var text = new StringBuilder("timestamp,tag,value,quality,revision\r\n");
        foreach (var point in Query(path, start, end)) text.Append(point.Timestamp.ToString("O", CultureInfo.InvariantCulture)).Append(',').Append(Cell(path)).Append(',').Append(Cell(point.Value.Kind == ScalarKind.Number ? point.Value.Number.ToString("R", CultureInfo.InvariantCulture) : point.Value.ToString(), point.Value.Kind != ScalarKind.Number)).Append(',').Append(point.Quality).Append(',').Append(point.Revision).Append("\r\n");
        return text.ToString();
    }
    public void Dispose() => _subscription.Dispose();
}
