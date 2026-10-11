using System.Runtime.CompilerServices;
using ScadaSpace.Alarms;
using ScadaSpace.Core;
using ScadaSpace.History;
using ScadaSpace.Tags;
using static Suite;

internal static class HistoryAlarmReviewTests
{
    private const string Path = "[default]Value";
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class CountingProvider(TagStore inner) : ITagProvider
    {
        public int Reads;
        public QualifiedValue Read(string path) { Reads++; return inner.Read(path); }
        public IReadOnlyList<TagDefinition> Browse(string prefix = "") => inner.Browse(prefix);
        public WriteResult Write(string path, Scalar value, string actor, long? expectedRevision = null) => inner.Write(path, value, actor, expectedRevision);
        public IDisposable Subscribe(Action<TagChange> listener) => inner.Subscribe(listener);
    }
    [ModuleInitializer]
    internal static void Register()
    {
        Add("history binary windows match a reference scan across ring wrap and timestamp ties", () => {
            var clock = new Clock(); var tags = new TagStore([new() { Path = Path }], clock: clock); using var history = new Historian(tags, 17);
            for (var i = 0; i < 95; i++) { clock.Now = DateTimeOffset.UnixEpoch.AddSeconds(i / 3); tags.Publish(Path, Scalar.From(i)); }
            var all = history.Query(Path); Equal(17, all.Length);
            for (var start = 23; start < 35; start++) for (var end = start; end < 36; end++)
            {
                var a = DateTimeOffset.UnixEpoch.AddSeconds(start); var b = DateTimeOffset.UnixEpoch.AddSeconds(end);
                Assert(all.Where(p => p.Timestamp >= a && p.Timestamp <= b).SequenceEqual(history.Query(Path, a, b)));
            }
            Equal(all.Length, history.Count(Path)); Equal(all[^1], history.Latest(Path)!.Value);
        });
        Add("history snapshots do not alias storage and empty lookups are explicit", () => {
            var tags = new TagStore([new() { Path = Path }]); using var history = new Historian(tags, 5);
            var copy = history.Query(Path); copy[0] = QualifiedValue.Good(Scalar.From(99), DateTimeOffset.UtcNow);
            Equal(0d, history.Latest(Path)!.Value.Value.Number); Equal(0, history.Count("[default]Missing")); Assert(history.Latest("[default]Missing") is null);
            Equal(0, history.Query(Path, DateTimeOffset.MaxValue).Length);
            Throws<ArgumentException>(() => history.Query(Path, DateTimeOffset.MaxValue, DateTimeOffset.MinValue));
        });
        Add("history CSV preserves binary64 precision and protects whitespace-prefixed formulas", () => {
            var tags = new TagStore([new() { Path = Path, InitialValue = Scalar.From(-1.23456789012345) }, new() { Path = "[default]Text", DataType = ScalarKind.String, InitialValue = Scalar.From("  =1+1") }]);
            using var history = new Historian(tags);
            Assert(history.ExportCsv(Path).Contains("\"-1.23456789012345\""));
            Assert(history.ExportCsv("[default]Text").Contains("\"'  =1+1\""));
        });
        Add("zero-deadband high and low alarms remain active at their setpoint", () => {
            foreach (var mode in new[] { AlarmMode.High, AlarmMode.Low })
            {
                var tags = new TagStore([new() { Path = Path, InitialValue = Scalar.From(50) }]);
                using var alarms = new AlarmEngine(tags, [new() { Id = "limit", TagPath = Path, Mode = mode, Setpoint = 50, Deadband = 0 }]);
                var id = alarms.Status().Single().EventId;
                for (var i = 0; i < 20; i++) { alarms.Evaluate(); tags.Publish(Path, Scalar.From(50)); }
                Equal(1, alarms.Events().Length); Equal(id, alarms.Status().Single().EventId); Assert(alarms.Status().Single().Active); Equal(1, alarms.Journal().Length);
                tags.Publish(Path, Scalar.From(mode == AlarmMode.High ? 49 : 51)); Assert(!alarms.Status().Single().Active);
            }
        });
        Add("unrelated tag writes do not rescan all alarm inputs", () => {
            var definitions = Enumerable.Range(0, 300).Select(i => new TagDefinition { Path = "[default]Tag" + i }).ToArray();
            var tags = new TagStore(definitions); var counted = new CountingProvider(tags);
            using var alarms = new AlarmEngine(counted, definitions.Take(299).Select((d,i) => new AlarmDefinition { Id = "alarm" + i, TagPath = d.Path }));
            counted.Reads = 0; tags.Publish(definitions[299].Path, Scalar.From(1)); Equal(0, counted.Reads);
            tags.Publish(definitions[0].Path, Scalar.From(99)); Equal(1, counted.Reads); Equal(1, alarms.Status().Length);
            counted.Reads = 0; alarms.Evaluate(); Equal(299, counted.Reads);
        });
        Add("timer evaluation still advances delayed alarms after unrelated writes", () => {
            var clock = new Clock(); var tags = new TagStore([new() { Path = Path }, new() { Path = "[default]Other" }], clock: clock);
            using var alarms = new AlarmEngine(tags, [new() { TagPath = Path, Setpoint = 50, ActiveDelaySeconds = 3 }], clock: clock);
            tags.Publish(Path, Scalar.From(60)); clock.Now = clock.Now.AddSeconds(4); tags.Publish("[default]Other", Scalar.From(1)); Equal(0, alarms.Status().Length);
            alarms.Evaluate(); Assert(alarms.Status().Single().Active);
        });
        Add("nonnumeric good-quality input resets an alarm pending delay", () => {
            var clock = new Clock(); var tags = new TagStore([new() { Path = Path, DataType = ScalarKind.String, InitialValue = Scalar.From("0") }], clock: clock);
            using var alarms = new AlarmEngine(tags, [new() { TagPath = Path, Setpoint = 50, ActiveDelaySeconds = 3 }], clock: clock);
            tags.Publish(Path, Scalar.From("90")); clock.Now = clock.Now.AddSeconds(2); tags.Publish(Path, Scalar.From("invalid"));
            clock.Now = clock.Now.AddSeconds(2); tags.Publish(Path, Scalar.From("90")); Equal(0, alarms.Status().Length);
            clock.Now = clock.Now.AddSeconds(3); alarms.Evaluate(); Assert(alarms.Status().Single().Active);
        });
    }
}
