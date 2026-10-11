using ScadaSpace.Core;

namespace ScadaSpace.History;

public sealed record HistoryQueryRequest(string Path, DateTimeOffset Start, DateTimeOffset End, AggregateKind? Aggregate = null, int ReturnSize = 100, int Limit = 1000)
{
    public void Validate()
    {
        if (!TagPaths.IsValid(Path) || Start > End || Aggregate.HasValue && (Start == End || !Enum.IsDefined(Aggregate.Value)) || ReturnSize is < 1 or > 1500 || Limit is < 1 or > 1500)
            throw new ArgumentException("Invalid history query, time interval or row limit (1–1500).");
    }
}
public sealed record HistoryQueryResult(HistoryStamp Stamp, Scalar Data);

/// <summary>Bounded shared cache of owned tall document results. This is local memory history, not a Gateway/SQL query service.</summary>
public sealed class HistoryQueryCache
{
    private readonly object _gate = new();
    private readonly Dictionary<HistoryQueryRequest, HistoryQueryResult> _entries = [];
    private readonly Queue<HistoryQueryRequest> _order = new();
    private readonly int _capacity;
    public Historian History { get; }
    public long Reads { get; private set; }
    public long Hits { get; private set; }
    public int Count { get { lock (_gate) return _entries.Count; } }
    public HistoryQueryCache(Historian history, int capacity = 32)
    {
        History = history ?? throw new ArgumentNullException(nameof(history));
        _capacity = capacity is >= 1 and <= 128 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    }
    public HistoryQueryResult Get(HistoryQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request); request.Validate();
        lock (_gate)
        {
            var info = History.GetSeriesInfo(request.Path);
            if (info.Last is null) throw new InvalidOperationException("No local history is available for " + request.Path);
            if (_entries.TryGetValue(request, out var previous) && previous.Stamp == info.Stamp) { Hits++; return previous; }
            HistoryStamp stamp; Scalar data;
            if (request.Aggregate is { } kind)
            {
                var result = History.ReadAggregateWindow(request.Path, request.Start, request.End, request.ReturnSize, kind);
                stamp = result.Stamp;
                data = Scalar.Array(result.Buckets.Select(b => Row(request.Path, b.Start, b.Value, b.Quality, b.Count)));
            }
            else
            {
                var result = History.ReadWindow(request.Path, request.Start, request.End, maxSamples: request.Limit);
                stamp = result.Stamp;
                data = Scalar.Array(result.Samples.Select(s => Row(request.Path, s.Timestamp, s.Value, s.Quality, 1)));
            }
            var query = new HistoryQueryResult(stamp, data); Reads++;
            if (!_entries.ContainsKey(request))
            {
                while (_entries.Count >= _capacity) _entries.Remove(_order.Dequeue());
                _order.Enqueue(request);
            }
            _entries[request] = query; return query;
        }
    }
    private static Scalar Row(string path, DateTimeOffset time, Scalar value, Quality quality, int count) => Scalar.Object(new Dictionary<string, Scalar>
    {
        ["timestamp"] = Scalar.From(time.ToUnixTimeMilliseconds()), ["path"] = Scalar.From(path),
        ["value"] = value, ["quality"] = Scalar.From(quality.ToString()), ["count"] = Scalar.From(count)
    });
    public void Clear() { lock (_gate) { _entries.Clear(); _order.Clear(); } }
}
