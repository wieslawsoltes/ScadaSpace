using ScadaSpace.Core;

namespace ScadaSpace.History;

public sealed record HistoryAggregateWindow(HistoryStamp Stamp, IReadOnlyList<HistoryBucket> Buckets);

public sealed partial class Historian
{
    private struct Accumulator
    {
        public int Total, Good, Numbers;
        public double Mean, Min, Max;
        public Scalar Last;
        public void Add(QualifiedValue sample)
        {
            Total++;
            if (!sample.IsGood) return;
            Good++; Last = sample.Value;
            if (sample.Value.Kind != ScalarKind.Number) return;
            var n = sample.Value.Number; Numbers++;
            if (Numbers == 1) { Mean = Min = Max = n; return; }
            Min = Math.Min(Min, n); Max = Math.Max(Max, n);
            // Same-sign subtraction cannot overflow; opposite signs use scaled terms.
            Mean = Math.Sign(Mean) == Math.Sign(n) ? Mean + (n - Mean) / Numbers : Mean * ((Numbers - 1d) / Numbers) + n / Numbers;
        }
    }
    private static void ValidateAggregate(DateTimeOffset start, DateTimeOffset end, int count, AggregateKind kind)
    {
        if (count is < 1 or > 10000 || end <= start || !Enum.IsDefined(kind)) throw new ArgumentException("Invalid aggregation interval or kind.");
    }
    public HistoryAggregateWindow ReadAggregateWindow(string path, DateTimeOffset start, DateTimeOffset end, int bucketCount, AggregateKind kind)
    {
        ValidateAggregate(start, end, bucketCount, kind);
        lock (_gate)
        {
            _series.TryGetValue(path, out var ring);
            return new(ring is null ? default : Stamp(ring), Array.AsReadOnly(AggregateCore(ring, start, end, bucketCount, kind)));
        }
    }
    private static HistoryBucket[] AggregateCore(Ring? ring, DateTimeOffset start, DateTimeOffset end, int count, AggregateKind kind)
    {
        var accumulators = new Accumulator[count]; var ticks = (end - start).Ticks;
        if (ring is not null)
        {
            var first = ring.LowerBound(start); var last = ring.UpperBound(end);
            for (var i = first; i < last; i++)
            {
                var sample = ring.SampleAt(i);
                var index = Math.Min(count - 1, (int)((((Int128)(sample.Timestamp - start).Ticks + 1) * count - 1) / ticks));
                accumulators[index].Add(sample);
            }
        }
        var results = new HistoryBucket[count];
        for (var i = 0; i < count; i++)
        {
            var a = accumulators[i];
            var value = kind switch
            {
                AggregateKind.Count => Scalar.From(a.Good),
                AggregateKind.Last when a.Good > 0 => a.Last,
                AggregateKind.Average when a.Numbers > 0 => Scalar.From(a.Mean),
                AggregateKind.Minimum when a.Numbers > 0 => Scalar.From(a.Min),
                AggregateKind.Maximum when a.Numbers > 0 => Scalar.From(a.Max),
                _ => Scalar.Null
            };
            var usable = kind is AggregateKind.Count or AggregateKind.Last ? a.Good : a.Numbers;
            var available = kind == AggregateKind.Count || (kind == AggregateKind.Last ? a.Good : a.Numbers) > 0;
            var quality = !available ? Quality.BadNotFound : usable == a.Total ? Quality.Good : Quality.Uncertain;
            results[i] = new(start.AddTicks((long)((Int128)ticks * i / count)), start.AddTicks((long)((Int128)ticks * (i + 1) / count)), value, quality, a.Good);
        }
        return results;
    }
}
