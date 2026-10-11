using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ScadaSpace.Core;
using ScadaSpace.Designer;
using ScadaSpace.Data;
using ScadaSpace.History;
using ScadaSpace.Runtime;
using ScadaSpace.Tags;

// Relative microbenchmarks, not FPS or GPU benchmarks. Correctness and allocations gate CI;
// elapsed times are observations only, with no machine-dependent pass/fail threshold.
var reports = new List<Measurement>();
var clock = new Clock();
const string path = "[default]Value";
var tags = new TagStore([new() { Path = path }], clock: clock);
using var history = new Historian(tags, 16384);
for (var i = 0; i < 16000; i++) { clock.Now = DateTimeOffset.UnixEpoch.AddMilliseconds(i); tags.Publish(path, Scalar.From(i)); }
var start = clock.Now.AddMilliseconds(-31); var end = clock.Now;
QualifiedValue[] Scan() => history.Query(path).Where(p => p.Timestamp >= start && p.Timestamp <= end).ToArray();
QualifiedValue[] Window() => history.Query(path, start, end);
if (!Scan().SequenceEqual(Window()) || Window().Length != 32) throw new Exception("History comparison must have identical results.");
var historyScan = Measure("history_full_snapshot_then_filter", 100, () => Scan().Length);
var historyWindow = Measure("history_indexed_window", 100, () => Window().Length);
reports.AddRange([historyScan, historyWindow]);
if (historyWindow.BytesPerOperation >= historyScan.BytesPerOperation / 8) throw new Exception("Narrow history queries allocated a retained-series-sized snapshot.");

var cache = new HistoryWindowCache(history);
var plot = cache.Get(path, start, end, 20);
reports.Add(Measure("cached_plot_window", 1000, () => cache.Get(path, start, end, 20).Points.Count));
if (cache.Reads != 1 || reports[^1].BytesPerOperation != 0) throw new Exception("Unchanged plot windows must reuse owned snapshots without allocation.");

var aggregateStart = DateTimeOffset.UnixEpoch; const int bucketCount = 64;
HistoryBucket[] GroupReference()
{
    var ticks = (end - aggregateStart).Ticks;
    var groups = Enumerable.Range(0,bucketCount).Select(_ => new List<QualifiedValue>()).ToArray();
    foreach (var sample in history.Query(path, aggregateStart, end))
    {
        var index = Math.Min(bucketCount - 1, (int)((((Int128)(sample.Timestamp - aggregateStart).Ticks + 1) * bucketCount - 1) / ticks));
        groups[index].Add(sample);
    }
    return groups.Select((g,i) => new HistoryBucket(aggregateStart.AddTicks((long)((Int128)ticks*i/bucketCount)), aggregateStart.AddTicks((long)((Int128)ticks*(i+1)/bucketCount)),
        Scalar.From(g.Average(p => p.Value.Number)), Quality.Good, g.Count)).ToArray();
}
HistoryBucket[] Streamed() => history.Aggregate(path, aggregateStart, end, bucketCount, AggregateKind.Average);
var expectedBuckets = GroupReference(); var actualBuckets = Streamed();
for (var i=0;i<bucketCount;i++)
    if (expectedBuckets[i].Start != actualBuckets[i].Start || expectedBuckets[i].End != actualBuckets[i].End || expectedBuckets[i].Count != actualBuckets[i].Count || Math.Abs(expectedBuckets[i].Value.Number-actualBuckets[i].Value.Number)>1e-8)
        throw new Exception("Streaming aggregates differ from the grouping reference.");
var grouped = Measure("aggregate_snapshot_group_reference", 50, () => GroupReference().Length);
var streamed = Measure("aggregate_streamed_buckets", 50, () => Streamed().Length);
reports.AddRange([grouped, streamed]);
if (streamed.BytesPerOperation >= grouped.BytesPerOperation/4) throw new Exception("Streaming aggregate allocated per retained sample instead of per bucket.");

var queries = new HistoryQueryCache(history);
var request = new HistoryQueryRequest(path,start,end);
var query = queries.Get(request);
reports.Add(Measure("cached_history_document", 1000, () => queries.Get(request).Data.Items.Count));
if (queries.Reads != 1 || !ReferenceEquals(query,queries.Get(request)) || reports[^1].BytesPerOperation >= 1024) throw new Exception("Cached query rebuilt row data.");

var items = Scalar.Array(Enumerable.Range(0, 1200).Select(i => Scalar.Object(new Dictionary<string, Scalar> { ["label"] = Scalar.From("Item " + i), ["id"] = Scalar.From("item" + i) })));
var document = new DocumentTree(items); var state = new DocumentTreeState(document);
var expected = document.Roots.Select(n => new DocumentTreeRow(n, false)).ToArray();
if (!expected.SequenceEqual(state.Visible())) throw new Exception("Tree comparison must have identical results.");
var treeScan = Measure("tree_flat_projection_reference", 100, () => document.Roots.Select(n => new DocumentTreeRow(n, false)).ToArray().Length);
var treeCached = Measure("tree_cached_projection", 100, () => state.Visible().Count);
reports.AddRange([treeScan, treeCached]);
if (state.ProjectionBuildCount != 1 || treeCached.BytesPerOperation >= 64) throw new Exception("Idle tree projections must reuse their immutable snapshot.");

var root = new ComponentNode { Type = "Container" };
for (var i = 0; i < 500; i++) { root.Props["p" + i] = Scalar.From(0); root.Bindings["p" + i] = new() { Kind = BindingKind.Expression, Source = "1+1" }; }
var view = new ViewInstance(new() { Root = root }, tags); view.Evaluate();
var builds = view.BindingIndexBuildCount;
reports.Add(Measure("binding_evaluation_500_properties", 100, () => { view.Evaluate(); return view.Values.Count; }));
if (view.BindingIndexBuildCount != builds || view.Read(root, "p499").Value.Number != 2) throw new Exception("Binding evaluation must reuse its index and preserve results.");

var workspace = new DesignerWorkspace();
workspace.Reset(Enumerable.Range(0, 128).Select(i => "View" + i));
var openSnapshot = workspace.OpenViews; var workspaceRevision = workspace.Revision;
var activation = Measure("workspace_already_active_128_resources", 1000, () => { workspace.Open("View0"); return workspace.OpenViews.Count; });
reports.Add(activation);
if (!ReferenceEquals(openSnapshot, workspace.OpenViews) || workspace.Revision != workspaceRevision || activation.BytesPerOperation != 0)
    throw new Exception("Already-active resources must retain their snapshot without allocation.");

var configurationDocument = new DesignerDocument(DemoProject.Create());
var configurationIndex = new ConfigurationIndex(); configurationIndex.Refresh(configurationDocument);
var configurationRows = configurationIndex.Query("Tank");
int RebuildConfiguration()
{
    var index = new ConfigurationIndex(); index.Refresh(configurationDocument); return index.Query("Tank").Count;
}
if (RebuildConfiguration() != configurationRows.Count) throw new Exception("Configuration query reference differs.");
reports.Add(Measure("configuration_inventory_rebuild", 100, RebuildConfiguration));
var cachedConfiguration = Measure("configuration_inventory_cached", 1000, () => { configurationIndex.Refresh(configurationDocument); return configurationIndex.Query("Tank").Count; });
reports.Add(cachedConfiguration);
if (configurationIndex.BuildCount != 1 || configurationIndex.QueryBuildCount != 1 || cachedConfiguration.BytesPerOperation != 0)
    throw new Exception("Idle configuration lookup must retain its inventory and query without allocation.");

Directory.CreateDirectory("TestResults");
File.WriteAllText("TestResults/performance.json", JsonSerializer.Serialize(new {
    runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
    processorCount = Environment.ProcessorCount, measurements = reports,
    fixture = new { retainedHistory = history.Count(path), windowSamples = 32, treeItems = 1200, boundProperties = 500, aggregateBuckets = bucketCount, plotPoints = plot.Points.Count },
    note = "Five warmed batches per case. Reference strategies return the same fixture results. Times are not hardware-GPU, overall application FPS, or full-parity evidence."
}, new JsonSerializerOptions { WriteIndented = true }));
foreach (var report in reports) Console.WriteLine($"PERF {report.Name}: {report.MedianMicroseconds:F3} us/op, {report.BytesPerOperation:F0} B/op");
Console.WriteLine("PASS performance invariants and reference equivalence");

static Measurement Measure(string name, int iterations, Func<int> action)
{
    for (var i = 0; i < 30; i++) action();
    var times = new List<double>(); var allocations = new List<double>(); long checksum = 0;
    for (var batch = 0; batch < 5; batch++)
    {
        var bytes = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++) checksum += action();
        var elapsed = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
        allocations.Add((GC.GetAllocatedBytesForCurrentThread() - bytes) / (double)iterations); times.Add(elapsed / iterations);
    }
    times.Sort(); allocations.Sort();
    return new(name, iterations, times[2], allocations[2], checksum);
}
sealed record Measurement(string Name, int IterationsPerBatch, double MedianMicroseconds, double BytesPerOperation, long Checksum);
sealed class Clock : TimeProvider
{
    public DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    public override DateTimeOffset GetUtcNow() => Now;
}
