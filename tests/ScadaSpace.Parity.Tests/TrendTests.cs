using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using ScadaSpace.History;
using ScadaSpace.Tags;
using static Suite;

internal static class TrendTests
{
    private static readonly DateTimeOffset T = DateTimeOffset.UnixEpoch;
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = T; public override DateTimeOffset GetUtcNow() => Now; }
    private static QualifiedValue Point(int second, double value, Quality quality = Quality.Good, long revision = 1) => new(Scalar.From(value), quality, T.AddSeconds(second), revision);
    [ModuleInitializer]
    internal static void Register()
    {
        Add("plot snapshots include adjacent samples without changing inclusive Query semantics", () => {
            var clock = new Clock(); var tags = new TagStore([new() { Path = "[default]A" }], clock: clock); using var h = new Historian(tags, 8);
            for (var i = 1; i <= 20; i++) { clock.Now = T.AddSeconds(i); tags.Publish("[default]A", Scalar.From(i)); }
            var window = h.ReadWindow("[default]A", T.AddSeconds(16.5), T.AddSeconds(18.5), true);
            Assert(window.Samples.Select(p => p.Value.Number).SequenceEqual(new double[] { 16,17,18,19 }));
            Equal(2, h.Query("[default]A", window.Start, window.End).Length); Equal(8, window.Stamp.Count);
            Throws<NotSupportedException>(() => ((IList<QualifiedValue>)window.Samples)[0] = Point(0, 0));
            tags.Publish("[default]A", Scalar.From(50)); Equal(16d, window.Samples[0].Value.Number);
        });
        Add("plot windows are reused until samples, range or pixel budget change", () => {
            var clock = new Clock(); var tags = new TagStore([new() { Path = "[default]A" }], clock: clock); using var h = new Historian(tags); var cache = new HistoryWindowCache(h);
            var first = cache.Get("[default]A", T.AddSeconds(-2), T.AddSeconds(2), 10);
            for (var i = 0; i < 100; i++) Assert(ReferenceEquals(first, cache.Get("[default]A", first.Source.Start, first.Source.End, 10)));
            Equal(1L, cache.Reads); Equal(100L, cache.Hits);
            tags.Publish("[default]A", Scalar.From(5)); var second = cache.Get("[default]A", first.Source.Start, first.Source.End, 10); Assert(!ReferenceEquals(first, second));
            cache.Get("[default]A", first.Source.Start, first.Source.End, 20); Equal(3L, cache.Reads);
        });
        Add("window stamps change with timestamps even when externally supplied revisions repeat", () => {
            var tags = new TagStore([new() { Path = "[default]A" }], clock: new Clock()); using var h = new Historian(tags, 2);
            var cache = new HistoryWindowCache(h); h.Append("[default]A", Point(1,1)); var a = cache.Get("[default]A", T, T.AddSeconds(4), 10);
            h.Append("[default]A", Point(2,2)); var b = cache.Get("[default]A", T, T.AddSeconds(4), 10); Assert(a.Source.Stamp != b.Source.Stamp); Equal(2d, b.Source.Samples[^1].Value.Number);
        });
        Add("plot caches are bounded and reject invalid arguments before reuse", () => {
            using var h = new Historian(new TagStore([])); var cache = new HistoryWindowCache(h, 2);
            for (var i = 0; i < 8; i++) cache.Get("[default]"+i, T, T.AddSeconds(1), 5);
            Equal(2, cache.Count); Throws<ArgumentException>(() => cache.Get("x", T, T, 1)); Throws<ArgumentException>(() => cache.Get("x", T, T.AddSeconds(1), 0));
            cache.Clear(); Equal(0, cache.Count);
        });
        Add("trace interpolates original samples and selects the last timestamp revision", () => {
            var data = new[] { Point(0,0), Point(10,10), Point(10,20, revision:2), Point(20,40) };
            Equal(5d, HistoryTrace.At(data, T.AddSeconds(5)).Value.Number); Equal(20d, HistoryTrace.At(data, T.AddSeconds(10)).Value.Number); Equal(30d, HistoryTrace.At(data, T.AddSeconds(15)).Value.Number);
        });
        Add("trace does not extrapolate or cross bad-quality intervals", () => {
            var data = new[] { Point(0,0), Point(5,50, Quality.Stale), Point(10,10) };
            Equal(Quality.Stale, HistoryTrace.At(data,T.AddSeconds(3)).Quality); Equal(Quality.Stale, HistoryTrace.At(data,T.AddSeconds(7)).Quality);
            Equal(Quality.BadNotFound, HistoryTrace.At(data,T.AddSeconds(-1)).Quality); Equal(Quality.BadNotFound, HistoryTrace.At(data,T.AddSeconds(11)).Quality);
            Equal(10d, HistoryTrace.At(data,T.AddSeconds(10)).Value.Number);
        });
        Add("viewport holds its range while new process data arrives and resets explicitly", () => {
            var v = new TrendViewport(); v.Update(T.AddSeconds(100), 20); Equal(T.AddSeconds(80), v.Range.Start);
            v.Hold(); v.Update(T.AddSeconds(200),20); Equal(T.AddSeconds(100),v.Range.End); Assert(!v.IsLive);
            v.Reset(); Equal(T.AddSeconds(200),v.Range.End); Assert(v.IsLive);
        });
        Add("zoom preserves the cursor time and pan preserves the span", () => {
            var v = new TrendViewport(); v.Update(T.AddSeconds(100),20); var anchor = v.TimeAt(.25); v.Zoom(.5,.25);
            Equal(anchor,v.TimeAt(.25)); Equal(10d,v.Range.Seconds); v.Pan(-15); Equal(anchor.AddSeconds(-15),v.TimeAt(.25)); Equal(10d,v.Range.Seconds);
        });
        Add("range selection and changed base windows have deterministic reset behavior", () => {
            var v = new TrendViewport(); v.Select(T,T.AddSeconds(3)); Assert(!v.IsLive); v.Update(T.AddSeconds(50),120); Equal(3d,v.Range.Seconds);
            v.Update(T.AddSeconds(60),30); Assert(v.IsLive); Equal(30d,v.Range.Seconds);
        });
        Add("viewport bounds and invalid input cannot corrupt state", () => {
            var v = new TrendViewport(); v.Update(DateTimeOffset.MinValue,20); Equal(DateTimeOffset.MinValue,v.Range.Start); Equal(20d,v.Range.Seconds);
            v.Update(DateTimeOffset.MaxValue,20); v.Pan(100); Equal(DateTimeOffset.MaxValue,v.Range.End);
            var before = v.Range; Throws<ArgumentOutOfRangeException>(() => v.Zoom(double.NaN)); Throws<ArgumentOutOfRangeException>(() => v.Pan(double.PositiveInfinity));
            Throws<ArgumentException>(() => v.Select(T,T)); Equal(before,v.Range);
        });
        Add("decimated windows retain quality breaks and trace uses unreduced samples", () => {
            var clock = new Clock(); var tags = new TagStore([new() { Path = "[default]A" }], clock: clock); using var h = new Historian(tags);
            for (var i = 1; i < 100; i++) { clock.Now = T.AddSeconds(i); tags.Publish("[default]A",Scalar.From(i),i == 50 ? Quality.Stale : Quality.Good); }
            var window = new HistoryWindowCache(h).Get("[default]A",T,T.AddSeconds(99),2);
            Assert(window.Points.Count < window.Source.Samples.Count); Assert(window.Points.Any(p => p.Quality == Quality.Stale));
            Equal(23.5,HistoryTrace.At(window.Source.Samples,T.AddSeconds(23.5)).Value.Number);
        });
    }
}
