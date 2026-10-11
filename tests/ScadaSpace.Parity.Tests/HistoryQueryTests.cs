using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using ScadaSpace.History;
using ScadaSpace.Tags;
using static Suite;

internal static class HistoryQueryTests
{
    private const string Path = "[default]A";
    private static readonly DateTimeOffset T = DateTimeOffset.UnixEpoch;
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => T; }
    private static Historian Make(int count = 10)
    {
        var h = new Historian(new TagStore([new() { Path = Path, InitialValue = Scalar.From(0) }], clock: new Clock()), Math.Max(16,count + 10));
        for (var i = 1; i <= count; i++) h.Append(Path,new(Scalar.From(i),Quality.Good,T.AddSeconds(i),i+1));
        return h;
    }
    [ModuleInitializer]
    internal static void Register()
    {
        Add("streamed aggregate preserves finite means for maximal binary64 values", () => {
            using var h = Make(0);
            h.Append(Path,new(Scalar.From(double.MaxValue),Quality.Good,T.AddSeconds(1),2));
            h.Append(Path,new(Scalar.From(double.MaxValue),Quality.Good,T.AddSeconds(2),3));
            Equal(double.MaxValue,h.Aggregate(Path,T.AddSeconds(1),T.AddSeconds(2),1,AggregateKind.Average)[0].Value.Number);
        });
        Add("streamed aggregate handles opposite extremes without intermediate overflow", () => {
            using var h = Make(0);
            h.Append(Path,new(Scalar.From(double.MaxValue),Quality.Good,T.AddSeconds(1),2));
            h.Append(Path,new(Scalar.From(-double.MaxValue),Quality.Good,T.AddSeconds(2),3));
            Equal(0d,h.Aggregate(Path,T.AddSeconds(1),T.AddSeconds(2),1,AggregateKind.Average)[0].Value.Number);
        });
        Add("integer aggregate boundaries agree with sample bucket membership", () => {
            using var h = Make(0);
            h.Append(Path,new(Scalar.From(3),Quality.Good,T.AddTicks(3),2));
            h.Append(Path,new(Scalar.From(6),Quality.Good,T.AddTicks(6),3));
            h.Append(Path,new(Scalar.From(10),Quality.Good,T.AddTicks(10),4));
            var b = h.Aggregate(Path,T,T.AddTicks(10),3,AggregateKind.Count);
            Equal(T.AddTicks(3),b[0].End); Equal(T.AddTicks(6),b[1].End);
            Equal(1d,b[0].Value.Number); Equal(1d,b[1].Value.Number); Equal(2d,b[2].Value.Number);
        });
        Add("aggregate snapshots own their buckets and retain quality metadata", () => {
            using var h = Make(2); h.Append(Path,new(Scalar.From(9),Quality.Stale,T.AddSeconds(3),4));
            var result = h.ReadAggregateWindow(Path,T,T.AddSeconds(3),1,AggregateKind.Average);
            Equal(1d,result.Buckets[0].Value.Number); Equal(Quality.Uncertain,result.Buckets[0].Quality);
            Equal(4,result.Stamp.Count); Throws<NotSupportedException>(() => ((IList<HistoryBucket>)result.Buckets).Clear());
        });
        Add("last aggregate distinguishes a good null from an empty interval", () => {
            using var h = new Historian(new TagStore([new() { Path = Path, DataType = ScalarKind.Null, InitialValue = Scalar.Null }],clock:new Clock()));
            var present = h.Aggregate(Path,T,T.AddSeconds(1),1,AggregateKind.Last)[0];
            Equal(Scalar.Null,present.Value); Equal(Quality.Good,present.Quality);
            Equal(Quality.BadNotFound,h.Aggregate(Path,T.AddSeconds(2),T.AddSeconds(3),1,AggregateKind.Last)[0].Quality);
        });
        Add("bounded raw snapshots reject oversize windows before allocating sample copies", () => {
            using var h = Make(5000); var before = GC.GetAllocatedBytesForCurrentThread();
            Throws<InvalidOperationException>(() => h.ReadWindow(Path,T,T.AddSeconds(5000),maxSamples:10));
            Assert(GC.GetAllocatedBytesForCurrentThread()-before < 100000);
            Equal(5001,h.Count(Path));
        });
        Add("tall raw query rows carry exact values quality paths and counts", () => {
            using var h = Make(3); var cache = new HistoryQueryCache(h);
            var result = cache.Get(new(Path,T.AddSeconds(1),T.AddSeconds(3)));
            Equal(3,result.Data.Items.Count); var row = result.Data.Items[0].Fields;
            Equal(1000d,row["timestamp"].Number); Equal(Path,row["path"].Text); Equal(1d,row["value"].Number); Equal("Good",row["quality"].Text); Equal(1d,row["count"].Number);
        });
        Add("history query caches share immutable data and invalidate on accepted append", () => {
            using var h = Make(2); var cache = new HistoryQueryCache(h); var request = new HistoryQueryRequest(Path,T,T.AddSeconds(10));
            var a=cache.Get(request); Assert(ReferenceEquals(a,cache.Get(request))); Equal(1L,cache.Hits);
            h.Append(Path,new(Scalar.From(3),Quality.Good,T.AddSeconds(3),4)); var b=cache.Get(request);
            Assert(!ReferenceEquals(a,b)); Equal(3,a.Data.Items.Count); Equal(4,b.Data.Items.Count); Equal(2L,cache.Reads);
        });
        Add("query cache keys include interval aggregate sizes and limits", () => {
            using var h=Make(4); var cache=new HistoryQueryCache(h,2);
            var request=new HistoryQueryRequest(Path,T,T.AddSeconds(4),AggregateKind.Average,1);
            Equal(2d,cache.Get(request).Data.Items[0].Fields["value"].Number);
            Equal(2,cache.Get(request with {ReturnSize=2}).Data.Items.Count);
            cache.Get(request with {Aggregate=AggregateKind.Maximum}); Equal(2,cache.Count);
            Throws<InvalidOperationException>(() => cache.Get(request with {Aggregate=null,Limit=1}));
            Equal(2,cache.Count); cache.Clear(); Equal(0,cache.Count);
        });
        Add("invalid history requests cannot reuse or populate cache entries", () => {
            using var h=Make(1); var cache=new HistoryQueryCache(h); var request=new HistoryQueryRequest(Path,T,T.AddSeconds(1));
            Throws<ArgumentException>(() => cache.Get(request with {ReturnSize=1501}));
            Throws<ArgumentException>(() => cache.Get(request with {End=T.AddSeconds(-1)}));
            Throws<InvalidOperationException>(() => cache.Get(request with {Path="[default]Missing"})); Equal(0,cache.Count);
        });
        Add("concurrent query readers see matching stamps and immutable row snapshots", () => {
            var tags=new TagStore([new() {Path=Path}]); using var h=new Historian(tags,1000); var cache=new HistoryQueryCache(h);
            var request=new HistoryQueryRequest(Path,DateTimeOffset.MinValue,DateTimeOffset.MaxValue);
            var writer=Task.Run(() => {for(var i=0;i<200;i++) tags.Publish(Path,Scalar.From(i));});
            Parallel.For(0,100,_ => { var result=cache.Get(request); Equal(result.Stamp.Count,result.Data.Items.Count); }); writer.GetAwaiter().GetResult();
            Equal(201,cache.Get(request).Data.Items.Count);
        });
    }
}
