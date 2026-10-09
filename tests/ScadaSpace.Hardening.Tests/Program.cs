using System.Text.Json;
using ScadaSpace.Core;
using ScadaSpace.Designer;
using ScadaSpace.History;
using ScadaSpace.Runtime;
using ScadaSpace.Tags;

var results = new List<object>(); var failed = 0; var count = 0;
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Throws<T>(Action body) where T : Exception { try { body(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
void Test(string name, Action body)
{
    count++;
    try { body(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; results.Add(new { name, passed = false, error = ex.ToString() }); Console.WriteLine("FAIL " + name + "\n" + ex); }
}
Test("explicit scalar wire format round-trips every kind", () =>
{
    foreach (var scalar in new[] { Scalar.Null, Scalar.From(68.4), Scalar.From(true), Scalar.From(false), Scalar.From("Tänk 01\nvalue") })
    {
        var text = JsonSerializer.Serialize(scalar, ProjectCodec.Options);
        Check(JsonSerializer.Deserialize<Scalar>(text, ProjectCodec.Options) == scalar, text);
    }
    Check(JsonSerializer.Deserialize<Scalar>("{\"kind\":\"Number\",\"number\":3.5}", ProjectCodec.Options).Number == 3.5);
});
Test("malformed scalar JSON is rejected before construction", () =>
{
    foreach (var text in new[] { "{}", "null", "3", "{\"kind\":\"Unknown\"}", "{\"kind\":\"Boolean\",\"boolean\":3}", "{\"kind\":\"Number\",\"number\":1e999}", "{\"kind\":\"String\",\"text\":null}", "{\"kind\":\"Null\",\"Kind\":\"Number\"}" }) Throws<JsonException>(() => JsonSerializer.Deserialize<Scalar>(text, ProjectCodec.Options));
});
Test("numeric format precision cannot request unbounded allocation", () =>
{
    Check(NumberFormatting.Format(3.14, "0.0") == "3.1");
    Check(NumberFormatting.Format(3.14, "F2") == "3.14");
    foreach (var format in new[] { "F999999999", "N1000000", new string('0', 200) }) Throws<FormatException>(() => NumberFormatting.Format(1, format));
});
Test("direct bindings preserve source failure quality", () =>
{
    using var runtime = new ProjectRuntime(DemoProject.Create());
    var instance = runtime.CreateView("Overview"); var tank = instance.View.Root.Children.Single(n => n.Name == "Tank01");
    runtime.Tags.Publish("[default]Plant/Tank01/Level", Scalar.From(64), Quality.Stale); instance.Evaluate();
    Check(instance.Read(tank, "value").Quality == Quality.Stale);
    tank.Bindings["value"].Source = "[default]Missing"; instance.Evaluate(); Check(instance.Read(tank, "value").Quality == Quality.BadNotFound);
});
Test("bad quality gaps survive history decimation", () =>
{
    var start = DateTimeOffset.UtcNow;
    var samples = Enumerable.Range(0, 1000).Select(i => new QualifiedValue(Scalar.From(i == 533 ? 100 : 1), i is >= 501 and <= 506 ? Quality.Stale : Quality.Good, start.AddMilliseconds(i), i)).ToArray();
    var reduced = Historian.Decimate(samples, 10);
    Check(reduced.Any(s => s.Quality == Quality.Stale)); Check(reduced.Any(s => s.Value.Number == 100));
    Check(reduced.Any(s => s.Revision == 500) && reduced.Any(s => s.Revision == 507));
});
Test("reentrant writes notify subscribers in committed revision order", () =>
{
    var tags = new TagStore([new() { Path = "[default]Value", InitialValue = Scalar.From(0) }]);
    var seen = new List<long>();
    using var a = tags.Subscribe(change => { if (change.Current.Value.Number < 3) tags.Publish(change.Path, Scalar.From(change.Current.Value.Number + 1)); });
    using var b = tags.Subscribe(change => seen.Add(change.Current.Revision));
    tags.Publish("[default]Value", Scalar.From(1));
    Check(seen.Count == 3); Check(seen.SequenceEqual(seen.OrderBy(x => x)));
});
Test("parallel writes retain ordered notifications", () =>
{
    var tags = new TagStore([new() { Path = "[default]Value", InitialValue = Scalar.From(0) }]);
    var seen = new List<long>(); using var subscription = tags.Subscribe(change => { lock (seen) seen.Add(change.Current.Revision); });
    Parallel.For(0, 1000, i => tags.Publish("[default]Value", Scalar.From(i)));
    Check(SpinWait.SpinUntil(() => { lock (seen) return seen.Count == 1000; }, TimeSpan.FromSeconds(3)));
    Check(seen.SequenceEqual(seen.OrderBy(x => x)));
});
Test("observer failures cannot split document history from model state", () =>
{
    var doc = new DesignerDocument(DemoProject.Create()); var before = doc.Project.Name;
    void BrokenObserver() => throw new InvalidOperationException("Observer failed");
    doc.Changed += BrokenObserver;
    Throws<InvalidOperationException>(() => doc.Execute("Rename", p => p.Name = "Committed"));
    Check(doc.Project.Name == "Committed" && doc.CanUndo && doc.IsDirty);
    doc.Changed -= BrokenObserver; Check(doc.Undo()); Check(doc.Project.Name == before && !doc.IsDirty);
});
Test("failed edits restore selection and project atomically", () =>
{
    var doc = new DesignerDocument(DemoProject.Create()); var id = doc.Project.Views[0].Root.Children[0].Id; doc.Select(id);
    Throws<InvalidOperationException>(() => doc.Execute("Invalid", p => { doc.Selection.Clear(); p.Views.Clear(); }));
    Check(doc.Selection.SetEquals([id]) && doc.Project.Views.Count == 5 && !doc.CanUndo);
});
Test("savepoints remain correct across undo redo and divergent edits", () =>
{
    var doc = new DesignerDocument(DemoProject.Create()); doc.Execute("A", p => p.Name = "A"); doc.MarkSaved();
    doc.Execute("B", p => p.Name = "B"); Check(doc.IsDirty); doc.Undo(); Check(!doc.IsDirty);
    doc.Undo(); Check(doc.IsDirty); doc.Redo(); Check(!doc.IsDirty); doc.Execute("C", p => p.Name = "C"); Check(doc.IsDirty && !doc.CanRedo);
    doc.MarkUnsaved(); Check(doc.IsDirty);
});
Directory.CreateDirectory("TestResults"); File.WriteAllText("TestResults/hardening-results.json", JsonSerializer.Serialize(new { tests = count, failures = failed, results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"RESULT {count - failed}/{count} passed"); return failed == 0 ? 0 : 1;
