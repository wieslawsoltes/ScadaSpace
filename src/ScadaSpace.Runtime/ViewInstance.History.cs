using System.Globalization;
using ScadaSpace.Core;
using ScadaSpace.History;

namespace ScadaSpace.Runtime;

public sealed partial class ViewInstance
{
    private HistoryQueryCache? _historyQueries;
    private readonly Dictionary<string, Scalar> _historyPlans = new(StringComparer.Ordinal);
    public long HistoryQueryReads => _historyQueries?.Reads ?? 0;
    public long HistoryQueryHits => _historyQueries?.Hits ?? 0;
    public ViewInstance WithHistory(Historian history, HistoryQueryCache? shared = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (shared is not null && !ReferenceEquals(shared.History, history)) throw new ArgumentException("The query cache belongs to another historian.");
        _historyQueries = shared ?? new(history); Invalidate(); return this;
    }
    private Scalar ReadHistory(string source, Func<string, Scalar> resolve)
    {
        if (_historyQueries is null) throw new InvalidOperationException("No historian is attached to this view.");
        if (!_historyPlans.TryGetValue(source, out var plan))
        {
            plan = Scalar.FromJson(source);
            if (plan.Kind != ScalarKind.Object || plan.Fields.Keys.Any(k => k is not ("path" or "start" or "end" or "windowSeconds" or "aggregate" or "returnSize" or "limit"))) throw new FormatException("Invalid or unknown Tag History query fields.");
            if (_historyPlans.Count >= 32) _historyPlans.Clear();
            _historyPlans[source] = plan;
        }
        var fields = plan.Fields;
        if (!fields.TryGetValue("path", out var pathValue) || pathValue.Kind != ScalarKind.String) throw new FormatException("Tag History requires a path string.");
        var path = ResolveTagPath(pathValue.Text!);
        Scalar Eval(Scalar value) => value.Kind == ScalarKind.String ? _expressions.Evaluate(value.Text!, resolve) : value;
        double Number(Scalar value)
        {
            var result = Eval(value);
            if (result.Kind != ScalarKind.Number) throw new FormatException("History query numeric fields require numbers or numeric expressions.");
            return result.Number;
        }
        int Integer(string key, int fallback)
        {
            var n = Number(fields.GetValueOrDefault(key, Scalar.From(fallback)));
            if (n < 1 || n > 1500 || n != Math.Truncate(n)) throw new FormatException("History query row counts must be integers from 1 to 1500.");
            return (int)n;
        }
        DateTimeOffset Time(Scalar value)
        {
            if (value.Kind == ScalarKind.String && DateTimeOffset.TryParseExact(value.Text, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date.ToUniversalTime();
            var n = Number(value);
            if (n != Math.Truncate(n) || n < -62135596800000d || n > 253402300799999d) throw new FormatException("History timestamps require supported integral Unix milliseconds or round-trip ISO dates.");
            return DateTimeOffset.FromUnixTimeMilliseconds((long)n);
        }
        var end = fields.TryGetValue("end", out var endValue) ? Time(endValue) : _historyQueries.History.Latest(path)?.Timestamp ?? throw new InvalidOperationException("No local history is available for " + path);
        DateTimeOffset start;
        if (fields.TryGetValue("start", out var startValue))
        {
            if (fields.ContainsKey("windowSeconds")) throw new FormatException("Use start or windowSeconds, not both.");
            start = Time(startValue);
        }
        else
        {
            var seconds = Number(fields.GetValueOrDefault("windowSeconds", Scalar.From(120)));
            if (seconds < .001 || seconds > 86400) throw new FormatException("History windows must span 1 ms to one day.");
            start = end.AddTicks(-Math.Min(end.UtcTicks, (long)(seconds * TimeSpan.TicksPerSecond)));
        }
        var name = fields.GetValueOrDefault("aggregate", Scalar.From("Raw"));
        if (name.Kind != ScalarKind.String) throw new FormatException("History aggregate must be a string.");
        AggregateKind? aggregate = name.Text!.ToLowerInvariant() switch
        {
            "raw" => null, "sampleaverage" => AggregateKind.Average, "minimum" => AggregateKind.Minimum,
            "maximum" => AggregateKind.Maximum, "count" => AggregateKind.Count, "last" => AggregateKind.Last,
            _ => throw new FormatException("Unsupported aggregate. Use Raw, SampleAverage, Minimum, Maximum, Count or Last; time-weighted Average is not implemented.")
        };
        var request = new HistoryQueryRequest(path, start, end, aggregate, Integer("returnSize", 100), Integer("limit", 1000));
        return _historyQueries.Get(request).Data;
    }
}
