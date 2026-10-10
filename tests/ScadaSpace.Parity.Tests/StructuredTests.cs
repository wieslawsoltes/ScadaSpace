using System.Runtime.CompilerServices;
using System.Text.Json;
using ScadaSpace.Core;
using ScadaSpace.Runtime;
using ScadaSpace.Tags;
using static Suite;

internal static class StructuredTests
{
    [ModuleInitializer]
    internal static void Register()
    {
        Add("objects compare structurally independent of member order", () => {
            var a = Scalar.FromJson("{\"b\":[1,true,null],\"a\":\"x\"}"); var b = Scalar.FromJson("{\"a\":\"x\",\"b\":[1,true,null]}");
            Equal(a, b); Equal(a.GetHashCode(), b.GetHashCode()); Equal(a.ToJson(), b.ToJson());
        });
        Add("array order participates in equality", () => Assert(Scalar.FromJson("[1,2]") != Scalar.FromJson("[2,1]")));
        Add("immutable object and array snapshots do not alias callers", () => {
            var values = new[] { Scalar.From(1) }; var a = Scalar.Array(values); values[0] = Scalar.From(9); Equal(1d, a.Items[0].Number);
            var fields = new Dictionary<string, Scalar> { ["a"] = a }; var o = Scalar.Object(fields); fields["a"] = Scalar.Null; Equal(a, o.Fields["a"]);
            Throws<NotSupportedException>(() => ((IList<Scalar>)a.Items)[0] = Scalar.Null);
            Throws<NotSupportedException>(() => ((IDictionary<string, Scalar>)o.Fields)["a"] = Scalar.Null);
        });
        Add("tagged structured wire format round-trips", () => {
            var v = Scalar.FromJson("{\"line\":\"A\",\"readings\":[12.5,false,null,{\"unit\":\"°C\"}]}");
            Equal(v, JsonSerializer.Deserialize<Scalar>(JsonSerializer.Serialize(v, ProjectCodec.Options), ProjectCodec.Options));
        });
        Add("legacy scalar payloads remain readable", () => Equal(Scalar.From(7), JsonSerializer.Deserialize<Scalar>("{\"kind\":\"Number\",\"number\":7}", ProjectCodec.Options)));
        Add("duplicate object keys are rejected", () => Throws<ArgumentException>(() => Scalar.FromJson("{\"x\":1,\"x\":2}")));
        Add("malformed tagged structured values are rejected", () => {
            Throws<JsonException>(() => JsonSerializer.Deserialize<Scalar>("{\"kind\":\"Array\",\"items\":{}}", ProjectCodec.Options));
            Assert(!new Scalar(ScalarKind.Array).IsValid());
            Assert(!new Scalar(ScalarKind.Object, Structured: Scalar.Array([]).Structured).IsValid());
        });
        Add("property graph depth is bounded", () => {
            var v = Scalar.From(1); for (var i = 0; i < StructuredValue.MaximumDepth; i++) v = Scalar.Array([v]);
            Throws<ArgumentException>(() => Scalar.Array([v]));
        });
        Add("property graph node count is bounded", () => Throws<ArgumentException>(() => Scalar.Array(Enumerable.Repeat(Scalar.Null, StructuredValue.MaximumNodes))));
        Add("nested property paths and quoted keys resolve", () => {
            var v = Scalar.FromJson("{\"rows\":[{\"a.b\":42,\"\":7}]}");
            Assert(PropertyPath.Parse("rows[0][\"a.b\"]").TryRead(v, out var x)); Equal(42d, x.Number);
            Assert(PropertyPath.Parse("rows[0][\"\"]").TryRead(v, out x)); Equal(7d, x.Number);
            Assert(!PropertyPath.Parse("rows[4].missing").TryRead(v, out _));
        });
        Add("nested writes are immutable and preserve sibling identity", () => {
            var root = Scalar.FromJson("{\"left\":{\"value\":1},\"right\":[2,3]}");
            var changed = PropertyPath.Parse("left.value").Set(root, Scalar.From(8));
            Equal(1d, root.Fields["left"].Fields["value"].Number); Equal(8d, changed.Fields["left"].Fields["value"].Number);
            Assert(ReferenceEquals(root.Fields["right"].Structured, changed.Fields["right"].Structured));
            Assert(ReferenceEquals(changed.Structured, PropertyPath.Parse("left.value").Set(changed, Scalar.From(8)).Structured));
        });
        Add("writes reject missing intermediate members and array holes", () => {
            var v = Scalar.FromJson("{\"rows\":[1]}");
            Throws<KeyNotFoundException>(() => PropertyPath.Parse("missing.value").Set(v, Scalar.From(2)));
            Throws<IndexOutOfRangeException>(() => PropertyPath.Parse("rows[3]").Set(v, Scalar.From(2)));
            Equal(2d, PropertyPath.Parse("newMember").Set(v, Scalar.From(2)).Fields["newMember"].Number);
        });
        Add("malformed and executable-looking paths are not evaluated", () => {
            foreach (var path in new[] { "", "a..b", "a[-1]", "a[", "a[0]b", "a.", "a[\"x\"", "a[1.5]" }) Throws<FormatException>(() => PropertyPath.Parse(path));
            Assert(!PropertyPath.Parse("constructor.prototype").TryRead(Scalar.Object([]), out _));
        });
        Add("project structured data persists without JSON-in-string encoding", () => {
            var project = DemoProject.Create(); project.Views[0].Root.Props["data"] = Scalar.FromJson("[{\"name\":\"Pump\",\"value\":12}]");
            var copy = ProjectCodec.Clone(project); Equal(project.Views[0].Root.Props["data"], copy.Views[0].Root.Props["data"]);
        });
        Add("structured tag writes retain typing and revisions", () => {
            var a = Scalar.FromJson("{\"value\":1}"); var store = new TagStore([new() { Path = "[default]Document", DataType = ScalarKind.Object, InitialValue = a }]);
            var old = store.Read("[default]Document"); var b = PropertyPath.Parse("value").Set(a, Scalar.From(2));
            Assert(store.Write("[default]Document", b, "operator", old.Revision).Success);
            Equal(WriteStatus.RevisionConflict, store.Write("[default]Document", a, "operator", old.Revision).Status);
            Equal(WriteStatus.BadType, store.Write("[default]Document", Scalar.From(2), "operator").Status);
        });
        Add("property type parser enforces object versus array", () => {
            Equal(ScalarKind.Array, Scalar.Parse("[1]", ScalarKind.Array).Kind);
            Throws<FormatException>(() => Scalar.Parse("[]", ScalarKind.Object));
        });
    }
}
