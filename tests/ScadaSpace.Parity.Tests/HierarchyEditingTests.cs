using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using ScadaSpace.Designer;
using ScadaSpace.Expressions;
using ScadaSpace.Layout;
using ScadaSpace.Runtime;
using static Suite;

internal static class HierarchyEditingTests
{
    private static ProjectDefinition Project()
    {
        var root = new ComponentNode { Id = "root-id", Name = "root", Type = "Container", Width = 600, Height = 500 };
        root.Props["padding"] = Scalar.From(10); root.Custom["number"] = Scalar.From(12);
        root.Children.Add(new() { Id = "a", Name = "A", Type = "NumericEntry", X = 20, Y = 30, Width = 100, Height = 40,
            Props = new() { ["value"] = Scalar.From(0) }, Bindings = new() { ["value"] = new() { Kind = BindingKind.Property, Source = "parent.custom.number", Bidirectional = true } } });
        root.Children.Add(new() { Id = "b", Name = "B", Type = "Label", X = 170, Y = 80, Width = 80, Height = 50,
            Props = new() { ["text"] = Scalar.From("B") }, Events = new() { ["onClick"] = [new() { Kind = ActionKind.WriteTag, Target = "[default]Value", Value = Scalar.From(9) }] } });
        root.Children.Add(new() { Id = "c", Name = "C", Type = "Label", X = 300, Y = 20, Width = 60, Height = 30 });
        return new() { Views = [new() { Path = "Main", Width = 600, Height = 500, Root = root }], Tags = [new() { Path = "[default]Value" }] };
    }
    private static Dictionary<string, RectD> Bounds(ProjectDefinition project) => LayoutEngine.Flatten(project.Views[0].Root, 600, 500).ToDictionary(x => x.Node.Id, x => x.Bounds);
    [ModuleInitializer]
    internal static void Register()
    {
        Add("wrapping siblings preserves screen geometry IDs z order and event content", () => {
            var d = new DesignerDocument(Project()); var before = Bounds(d.Project); d.Select("a"); d.Select("b", true);
            var id = d.WrapSelectionInCoordinateContainer(); var wrapper = d.Find(id)!; var after = Bounds(d.Project);
            Equal(before["a"], after["a"]); Equal(before["b"], after["b"]); Equal(before["c"], after["c"]);
            Assert(wrapper.Children.Select(x => x.Id).SequenceEqual(new[] { "a", "b" }));
            Assert(d.Project.Views[0].Root.Children.Select(x => x.Id).SequenceEqual(new[] { id, "c" }));
            Equal(0d, wrapper.Number("padding")); Equal(9d, d.Find("b")!.Events["onClick"][0].Value.Number);
            Assert(d.Selection.SetEquals(new[] { id })); Equal("Wrap in Coordinate Container", d.UndoLabel);
        });
        Add("wrap is one undoable operation and restores original parent bindings", () => {
            var d = new DesignerDocument(Project()); var original = ProjectCodec.Serialize(d.Project); d.Select("a"); var id = d.WrapSelectionInCoordinateContainer();
            Equal("root.custom.number", d.Find("a")!.Bindings["value"].Source); Equal(1L, d.Generation); Assert(d.CanUndo);
            d.Undo(); Equal(original, ProjectCodec.Serialize(d.Project)); Assert(d.Find(id) is null); Assert(!d.IsDirty);
            d.Redo(); Equal(id, d.FindParent("a")!.Id); Equal("root.custom.number", d.Find("a")!.Bindings["value"].Source);
        });
        Add("wrapped bidirectional parent properties still write to the original parent", () => {
            var d = new DesignerDocument(Project()); d.Select("a"); d.WrapSelectionInCoordinateContainer();
            using var runtime = new ProjectRuntime(d.Project); var view = runtime.CreateView("Main"); view.Evaluate();
            Equal(12d, view.Read(d.Find("a")!, "value").Value.Number);
            Assert(view.Write(d.Find("a")!, "value", Scalar.From(37), "test").Success);
            Equal(37d, view.Read(d.Project.Views[0].Root, "custom.number").Value.Number); Equal(12d, d.Project.Views[0].Root.Custom["number"].Number);
        });
        Add("wrapping preserves parent references in expressions transforms structures and history options", () => {
            var p = Project(); var a = p.Views[0].Root.Children[0];
            a.Bindings["expression"] = new() { Kind = BindingKind.Expression, Source = "parent.custom.number + {parent.custom.number}", Transforms = [new() { Kind = TransformKind.Expression, Expression = "value + {parent.custom.number}" }] };
            a.Bindings["structure"] = new() { Kind = BindingKind.ExpressionStructure, Source = """{"value":"{parent.custom.number}","literal":"'parent.custom.number'"}""" };
            a.Bindings["history"] = new() { Kind = BindingKind.TagHistory, Source = """{"path":"[default]Value","start":0,"end":4102444800000,"limit":"{parent.custom.number}"}""" };
            var d = new DesignerDocument(p); d.Select("a"); d.WrapSelectionInCoordinateContainer();
            var b = d.Find("a")!.Bindings; Equal("{root.custom.number} + {root.custom.number}", b["expression"].Source);
            Equal("value + {root.custom.number}", b["expression"].Transforms[0].Expression);
            Equal("'parent.custom.number'", Scalar.FromJson(b["structure"].Source).Fields["literal"].Text);
            Equal("{root.custom.number}", Scalar.FromJson(b["history"].Source).Fields["limit"].Text);
            using var runtime = new ProjectRuntime(d.Project); var view = runtime.CreateView("Main"); view.Evaluate();
            Equal(36d, view.Read(d.Find("a")!, "expression").Value.Number);
            Equal(12d, view.Read(d.Find("a")!, "structure").Value.Fields["value"].Number); Assert(view.Diagnostics.Count == 0);
        });
        Add("parent and child selection wraps only the selected ancestor without rebasing descendants", () => {
            var p = Project(); var group = p.Views[0].Root.Children[0]; group.Type = "Container";
            group.Children.Add(new() { Id = "nested", Name = "Nested", Type = "Label", Bindings = new() { ["text"] = new() { Kind = BindingKind.Property, Source = "parent.props.value" } } });
            var d = new DesignerDocument(p); d.Select("a"); d.Select("nested", true); var id = d.WrapSelectionInCoordinateContainer();
            Equal(1, d.Find(id)!.Children.Count); Equal("a", d.FindParent("nested")!.Id);
            Equal("parent.props.value", d.Find("nested")!.Bindings["text"].Source);
        });
        Add("wrapping nested siblings uses stable original parent identity and avoids duplicate names", () => {
            var p = Project(); var outer = new ComponentNode { Id = "parent-id", Name = "CoordinateContainer", Type = "Container" };
            outer.Custom["number"] = Scalar.From(18); var a = p.Views[0].Root.Children[0]; p.Views[0].Root.Children.RemoveAt(0); outer.Children.Add(a); p.Views[0].Root.Children.Insert(0, outer);
            var d = new DesignerDocument(p); d.Select("a"); var id = d.WrapSelectionInCoordinateContainer();
            Equal("CoordinateContainer2", d.Find(id)!.Name); Equal("parent-id.custom.number", d.Find("a")!.Bindings["value"].Source);
        });
        Add("wrapping negative positions and rotated leaves preserves exact child positions", () => {
            var p = Project(); var a = p.Views[0].Root.Children[0]; a.X = -30; a.Y = -20; a.Rotation = 90;
            var d = new DesignerDocument(p); var before = Bounds(d.Project)["a"]; d.Select("a"); var id = d.WrapSelectionInCoordinateContainer();
            var after = Bounds(d.Project)["a"]; Assert(Math.Abs(before.X - after.X) < 1e-9 && Math.Abs(before.Y - after.Y) < 1e-9);
            Assert(d.Find(id)!.Height >= a.Width - 1e-9); Equal(90d, d.Find("a")!.Rotation);
        });
        Add("noncontiguous locked percent and layout-managed wraps are rejected before mutation", () => {
            foreach (var mode in new[] { "noncontiguous", "locked", "percent", "layout", "root" })
            {
                var p = Project(); if (mode == "locked") p.Views[0].Root.Children[0].Locked = true;
                if (mode == "percent") p.Views[0].Root.Props["percentCoordinates"] = Scalar.From(true);
                if (mode == "layout") p.Views[0].Root.Layout = LayoutKind.FlexRow;
                var d = new DesignerDocument(p); d.Select(mode == "root" ? "root-id" : "a"); if (mode == "noncontiguous") d.Select("c", true);
                var before = ProjectCodec.Serialize(d.Project); Assert(!d.CanWrapSelection(out var reason) && reason is not null);
                Throws<InvalidOperationException>(() => d.WrapSelectionInCoordinateContainer()); Equal(before, ProjectCodec.Serialize(d.Project)); Equal(0L, d.Generation);
            }
        });
        Add("invalid affected expressions cannot leave a partially wrapped component tree", () => {
            var p = Project(); p.Views[0].Root.Children[1].Bindings["text"] = new() { Kind = BindingKind.Expression, Source = "{parent.custom.number}+" };
            var d = new DesignerDocument(p); d.Select("a"); d.Select("b", true); var before = ProjectCodec.Serialize(d.Project);
            Throws<ExpressionException>(() => d.WrapSelectionInCoordinateContainer()); Equal(before, ProjectCodec.Serialize(d.Project)); Assert(d.Selection.SetEquals(new[] { "a", "b" }));
        });
        Add("parser-based rewriting preserves quoted reference-like text and escapes", () => {
            const string source = "concat('literal {parent.custom.number}', '\\'quoted\\'', tostring({parent.custom.number}), tostring(parent.custom.number))";
            var rewritten = ExpressionEngine.RewriteReferences(source, path => path.StartsWith("parent.") ? "stable-id" + path[6..] : path);
            Assert(rewritten.Contains("'literal {parent.custom.number}'")); Assert(rewritten.Contains("'\\'quoted\\''"));
            var value = new ExpressionEngine().Evaluate(rewritten, path => { Equal("stable-id.custom.number", path); return Scalar.From(12); });
            Equal("literal {parent.custom.number}'quoted'1212", value.Text);
        });
        Add("reference rewriting distinguishes functions constants and operator words from reference occurrences", () => {
            var seen = new List<string>(); var source = "min(min, 3) + (true and false ? 1 : parent.custom.n)";
            var result = ExpressionEngine.RewriteReferences(source, path => { seen.Add(path); return path == "min" ? "other.props.n" : path; });
            Assert(seen.SequenceEqual(new[] { "min", "parent.custom.n" })); Assert(result.StartsWith("min({other.props.n}, 3)"));
        });
        Add("reference rewriting rejects syntax injection and retains unchanged string identity", () => {
            const string source = "{root.custom.x}+1"; Assert(ReferenceEquals(source, ExpressionEngine.RewriteReferences(source, x => x)));
            Throws<ExpressionException>(() => ExpressionEngine.RewriteReferences(source, _ => "bad} + {injected"));
            Throws<ExpressionException>(() => ExpressionEngine.RewriteReferences(source, _ => new string('x', 16384)));
            Throws<ExpressionException>(() => ExpressionEngine.RewriteReferences("(1+", x => x));
        });
        Add("hierarchy indexes reuse lookups and track removals undo redo and node moves", () => {
            var d = new DesignerDocument(Project()); var a = d.Find("a"); var builds = d.HierarchyBuildCount;
            for (var i = 0; i < 1000; i++) { Assert(ReferenceEquals(a, d.Find("a"))); Equal("root-id", d.FindParent("a")!.Id); Equal("Main", d.FindView("a")!.Path); }
            Equal(builds, d.HierarchyBuildCount); d.Select("a"); var group = d.WrapSelectionInCoordinateContainer(); Equal(group, d.FindParent("a")!.Id);
            d.Undo(); Equal("root-id", d.FindParent("a")!.Id); Assert(!ReferenceEquals(a, d.Find("a")));
            d.Redo(); Equal(group, d.FindParent("a")!.Id); d.Select(group); d.Delete(); Assert(d.Find("a") is null);
        });
        Add("rollback invalidates hierarchy object identity even without a generation change", () => {
            var d = new DesignerDocument(Project()); var a = d.Find("a");
            Throws<InvalidOperationException>(() => d.Execute("invalid", p => p.Views[0].Root.Children[0].Width = -1));
            Assert(!ReferenceEquals(a, d.Find("a"))); Equal(100d, d.Find("a")!.Width); Equal(0L, d.Generation);
        });
        Add("lookups inside a transaction observe newly added and removed nodes", () => {
            var d = new DesignerDocument(Project()); _ = d.Find("a");
            d.Execute("replace", p => { p.Views[0].Root.Children.RemoveAt(0); Assert(d.Find("a") is null); p.Views[0].Root.Children.Add(new() { Id = "new" }); Assert(d.Find("new") is not null); });
            Assert(d.Find("a") is null && d.Find("new") is not null);
        });
        Add("nested history operations fail atomically instead of corrupting undo history", () => {
            var d = new DesignerDocument(Project()); var before = ProjectCodec.Serialize(d.Project);
            Throws<InvalidOperationException>(() => d.Execute("outer", p => { p.Name = "changed"; d.Execute("nested", _ => { }); }));
            Equal(before, ProjectCodec.Serialize(d.Project)); Assert(!d.CanUndo);
            Throws<InvalidOperationException>(() => d.Execute("bad undo", _ => d.Undo())); Equal(0L, d.Generation);
        });
        Add("rollback notifies rebind consumers without masking the original error", () => {
            var d = new DesignerDocument(Project()); var project = d.Project; var observed = false;
            d.Restored += () => throw new Exception("rebind failed");
            d.Restored += () => { observed = true; Assert(!ReferenceEquals(project, d.Project)); Equal(100d, d.Find("a")!.Width); };
            Throws<InvalidOperationException>(() => d.Execute("invalid", p => p.Views[0].Root.Children[0].Width = -2));
            Assert(observed); Equal(1, d.RestoreDiagnostics.Count); Equal(0L, d.Generation); Assert(!d.IsDirty);
        });
        Add("layout cache reuses immutable geometry until root revision or dimensions change", () => {
            var root = Project().Views[0].Root; var cache = new LayoutSnapshotCache(); var initial = cache.Get(root, 600, 500, 0);
            Assert(initial.SequenceEqual(LayoutEngine.Flatten(root, 600, 500))); for (var i = 0; i < 1000; i++) Assert(ReferenceEquals(initial, cache.Get(root, 600, 500, 0)));
            Equal(1L, cache.BuildCount); root.Children[0].X += 10; var changed = cache.Get(root, 600, 500, 1);
            Equal(30d, initial[0].Bounds.X); Equal(40d, changed[0].Bounds.X); Assert(!ReferenceEquals(changed, cache.Get(root, 601, 500, 1)));
            Throws<NotSupportedException>(() => ((IList<LayoutSlot>)initial).Clear());
            var nextRoot = Project().Views[0].Root; Assert(!ReferenceEquals(changed, cache.Get(nextRoot, 600, 500, 1)));
        });
        Add("invalid layout requests retain the last cache and Clear releases it", () => {
            var root = Project().Views[0].Root; var cache = new LayoutSnapshotCache(); var old = cache.Get(root, 600, 500, 0);
            Throws<ArgumentOutOfRangeException>(() => cache.Get(root, double.NaN, 500, 0)); Assert(ReferenceEquals(old, cache.Get(root, 600, 500, 0)));
            cache.Clear(); Assert(!ReferenceEquals(old, cache.Get(root, 600, 500, 0))); Equal(2L, cache.BuildCount);
        });
    }
}
