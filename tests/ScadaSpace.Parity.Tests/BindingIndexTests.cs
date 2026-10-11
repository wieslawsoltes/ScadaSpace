using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using ScadaSpace.Runtime;
using ScadaSpace.Tags;
using static Suite;

internal static class BindingIndexTests
{
    private static (ComponentNode Root, ViewInstance Instance) Create()
    {
        var root = new ComponentNode { Type = "Container" };
        root.Props["data"] = Scalar.FromJson("{\"value\":1,\"other\":2}");
        root.Bindings["data.value"] = new() { Kind = BindingKind.Expression, Source = "7" };
        root.Props["output"] = Scalar.From(0);
        root.Bindings["output"] = new() { Kind = BindingKind.Property, Source = "this.props.data.value" };
        return (root, new(new() { Root = root }, new TagStore([])));
    }
    [ModuleInitializer]
    internal static void Register()
    {
        Add("binding indexes and parsed paths are reused across evaluations", () => {
            var (root, view) = Create(); view.Evaluate(); var builds = view.BindingIndexBuildCount; var parses = view.PropertyPathParseCount;
            for (var i = 0; i < 100; i++) { view.Evaluate(); Equal(7d, view.Read(root, "output").Value.Number); }
            Equal(builds, view.BindingIndexBuildCount); Equal(parses, view.PropertyPathParseCount); Equal(101L, view.EvaluationCount);
        });
        Add("mutable binding sources remain live without rebuilding structural indexes", () => {
            var (root, view) = Create(); view.Evaluate(); var builds = view.BindingIndexBuildCount;
            root.Bindings["data.value"].Source = "9"; view.Evaluate(); Equal(9d, view.Read(root, "output").Value.Number); Equal(builds, view.BindingIndexBuildCount);
            root.Bindings["data.value"].Enabled = false; view.Evaluate(); Assert(!view.Read(root, "data").IsGood);
        });
        Add("same-size binding replacement and key changes rebuild indexes", () => {
            var (root, view) = Create(); view.Evaluate(); var builds = view.BindingIndexBuildCount;
            root.Bindings["data.value"] = new() { Kind = BindingKind.Expression, Source = "12" }; view.Evaluate(); Equal(12d, view.Read(root, "output").Value.Number); Equal(builds + 1, view.BindingIndexBuildCount);
            root.Bindings.Remove("data.value"); root.Bindings["data.other"] = new() { Kind = BindingKind.Expression, Source = "20" }; view.Evaluate();
            Equal(1d, view.Read(root, "output").Value.Number); Equal(20d, view.Read(root, "data.other").Value.Number);
        });
        Add("replacement binding dictionary and descendant boundaries are respected", () => {
            var (root, view) = Create(); view.Evaluate(); root.Bindings = new() { ["data.other"] = new() { Kind = BindingKind.Expression, Source = "14" }, ["database"] = new() { Kind = BindingKind.Expression, Source = "99" } };
            view.Evaluate(); Equal(14d, view.Read(root, "data").Value.Fields["other"].Number); Equal(2, view.Read(root, "data").Value.Fields.Count); Equal(99d, view.Read(root, "database").Value.Number);
        });
        Add("quoted member paths and array leaves use their longest binding prefix", () => {
            var (root, view) = Create(); root.Props["rows"] = Scalar.FromJson("[{\"a.b\":1}]");
            root.Bindings["rows[0][\"a.b\"]"] = new() { Kind = BindingKind.Expression, Source = "42" };
            view.Evaluate(); Equal(42d, view.Read(root, "rows").Value.Items[0].Fields["a.b"].Number); Equal(0, view.Diagnostics.Count);
        });
        Add("legacy duplicate qualified aliases retain first-entry precedence", () => {
            var (root, view) = Create(); root.Bindings["props.output"] = new() { Kind = BindingKind.Expression, Source = "100" }; view.Evaluate(); Equal(7d, view.Read(root, "output").Value.Number);
        });
        Add("equal page context and parameter writes preserve cached values and revision", () => {
            var (root, view) = Create(); view.Parameters["x"] = Scalar.From(1); view.SetPageProperties(Scalar.FromJson("{\"path\":\"/\"}")); view.Evaluate(); var cached = view.Values.Count;
            view.SetPageProperties(Scalar.FromJson("{\"path\":\"/\"}")); Equal(cached, view.Values.Count);
            var first = view.WriteParameter("x", Scalar.From(1)); var second = view.WriteParameter("x", Scalar.From(1), first.Current.Revision); Equal(first.Current.Revision, second.Current.Revision); Equal(cached, view.Values.Count);
            view.SetPageProperties(Scalar.FromJson("{\"path\":\"/next\"}")); Equal(0, view.Values.Count);
        });
        Add("write capability follows property aliases to read-only and writable sources", () => {
            var (root, view) = Create(); root.Bindings["output"].Bidirectional = true; view.Evaluate();
            Assert(!view.CanWrite(root, "output"));
            root.Bindings.Remove("data.value"); view.Evaluate(); Assert(view.CanWrite(root, "output"));
            Assert(view.Write(root, "output", Scalar.From(30), "operator").Success); Equal(30d, view.Read(root, "data.value").Value.Number);
        });
        Add("parsed property path cache is bounded for arbitrary runtime reads", () => {
            var (root, view) = Create();
            for (var i = 0; i < 900; i++) view.Read(root, "data.missing" + i);
            Assert(view.CachedPropertyPathCount <= 512); Assert(view.PropertyPathParseCount >= 900);
        });
    }
}
