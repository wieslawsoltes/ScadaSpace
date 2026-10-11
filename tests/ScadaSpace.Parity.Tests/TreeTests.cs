using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using ScadaSpace.Data;
using static Suite;

internal static class TreeTests
{
    private static Scalar Source() => Scalar.FromJson("""
        [{"id":"plant","label":"Plant","expanded":true,"items":[
          {"id":"pump","label":"Pump","data":{"pressure":12},"items":[{"id":"motor","label":"Motor","data":"M1"}]},
          {"id":"tank","label":"Tank","data":"T1"}]}, {"id":"spare","label":"Spare"}]
        """);
    [ModuleInitializer]
    internal static void Register()
    {
        Add("tree parses ordered hierarchy and typed contextual data", () => {
            var tree = new DocumentTree(Source()); Equal(5, tree.Items.Count); Equal("Motor", tree.Get("0/0/0").Label); Equal(2, tree.Get("0/0/0").Depth); Equal(12d, tree.Get("0/0").Data.Fields["pressure"].Number);
            Equal("Plant,Pump,Motor,Tank,Spare", string.Join(',', tree.Items.Select(n => n.Label)));
        });
        Add("tree rejects non-array roots and malformed child data", () => {
            foreach (var json in new[] { "{}", "[1]", "[{\"label\":1}]", "[{\"items\":{}}]", "[{\"expanded\":\"yes\"}]", "[{\"id\":\"\"}]" }) Throws<ArgumentException>(() => new DocumentTree(Scalar.FromJson(json)));
        });
        Add("tree identities must be unique throughout the hierarchy", () => Throws<ArgumentException>(() => new DocumentTree(Scalar.FromJson("[{\"id\":\"x\",\"items\":[{\"id\":\"x\"}]}]"))));
        Add("duplicate labels are allowed and index paths remain distinct", () => {
            var tree = new DocumentTree(Scalar.FromJson("[{\"label\":\"x\"},{\"label\":\"x\"}]")); Assert(tree.Get("0").Key != tree.Get("1").Key);
        });
        Add("tree visibility reflects ancestor expansion", () => {
            var state = new DocumentTreeState(new(Source())); Equal(4, state.Visible().Count); state.SetExpanded("0/0", true); Equal(5, state.Visible().Count); state.SetExpanded("0", false); Equal(2, state.Visible().Count);
        });
        Add("tree expansion never mutates the immutable source", () => {
            var source = Source(); var state = new DocumentTreeState(new(source)); state.ExpandAll(false); Equal(2, state.Visible().Count); Assert(source.Items[0].Fields["expanded"].Boolean);
            state.ExpandAll(true); Equal(5, state.Visible().Count);
        });
        Add("tree filtering reveals matching ancestors without changing expansion", () => {
            var state = new DocumentTreeState(new(Source())); state.ExpandAll(false); Equal("Plant,Pump,Motor", string.Join(',', state.Visible("mOtOr").Select(r => r.Item.Label))); Equal(2, state.Visible().Count); Equal(0, state.Visible("not here").Count);
        });
        Add("tree reveal opens ancestors of a hidden node", () => {
            var state = new DocumentTreeState(new(Source())); state.ExpandAll(false); state.Reveal("0/0/0"); Assert(state.Visible().Any(r => r.Item.Label == "Motor"));
        });
        Add("tree selections expose numeric paths and original typed data", () => {
            var state = new DocumentTreeState(new(Source())); state.Select(["0/0", "1"], "0/0"); Equal("0/0", state.CurrentPath); Equal(2, state.Selected.Count);
            Equal(12d, state.SelectionData.Items[0].Fields["value"].Fields["pressure"].Number); Equal("[0,0]", state.SelectionData.Items[0].Fields["itemPath"].ToJson());
        });
        Add("tree selection validation is atomic", () => {
            var state = new DocumentTreeState(new(Source())); state.Select(["1"]); Throws<ArgumentException>(() => state.Select(["0", "5"])); Equal("1", state.CurrentPath);
            Throws<ArgumentException>(() => state.Select(["0"], "1")); Equal("1", state.CurrentPath);
        });
        Add("tree state survives reordered explicit identities", () => {
            var source = Source(); var state = new DocumentTreeState(new(source)); state.SetExpanded("0/0", true); state.Select(["0/0/0"]);
            state.Replace(new(Scalar.Array(source.Items.Reverse()))); Equal("1/0/0", state.CurrentPath); Assert(state.Visible().Any(r => r.Item.Label == "Motor"));
        });
        Add("tree removal prunes selections and expansion state", () => {
            var state = new DocumentTreeState(new(Source())); state.Select(["0/0"]); state.SetExpanded("0/0", true); state.Replace(new(Scalar.Array([]))); Equal("", state.CurrentPath); Equal(0, state.Selected.Count);
        });
        Add("changed incoming expansion flags override local expansion", () => {
            var source = Source(); var state = new DocumentTreeState(new(source)); state.SetExpanded("0", true);
            state.Replace(new(PropertyPath.Parse("[0].expanded").Set(source, Scalar.From(false)))); Equal(2, state.Visible().Count);
        });
        Add("tree branches use segment boundaries during recursive expansion", () => {
            var source = Scalar.Array(Enumerable.Range(0,11).Select(i => Scalar.FromJson("{\"items\":[{\"label\":\"child\"}]}")));
            var state = new DocumentTreeState(new(source)); state.SetExpanded("1", true, true); Assert(!state.IsExpanded(state.Tree.Get("10"))); Equal(12, state.Visible().Count);
        });
        Add("tree index paths are strict and not executable expressions", () => {
            var tree = new DocumentTree(Source()); foreach (var path in new[] { "01", "-1", "0//0", "0/../1", "this", "0/0/9" }) Throws<ArgumentException>(() => tree.Get(path));
        });
        Add("tree input count and filter sizes are bounded", () => {
            Throws<ArgumentException>(() => new DocumentTree(Scalar.Array(Enumerable.Repeat(Scalar.Object([]), DocumentTree.MaximumItems + 1))));
            Throws<ArgumentException>(() => new DocumentTreeState(new(Source())).Visible(new string('a',1025)));
        });
        Add("tree indexes expose read-only collections", () => {
            var tree = new DocumentTree(Source()); Throws<NotSupportedException>(() => ((IDictionary<string, DocumentTreeItem>)tree.ByPath).Clear());
            Throws<NotSupportedException>(() => ((IList<DocumentTreeItem>)tree.Roots).Clear());
        });
        Add("tree click payload preserves label and typed contextual data", () => {
            var data = new DocumentTree(Source()).Get("0/0").EventData; Equal("Pump", data.Fields["label"].Text); Equal("[0,0]", data.Fields["itemPath"].ToJson()); Equal(12d, data.Fields["data"].Fields["pressure"].Number);
        });
        Add("tag tree groups providers and sorts folder names deterministically", () => {
            var tags = new[] { new TagDefinition { Path = "[z]Pump/Pressure" }, new TagDefinition { Path = "[a]Tank/Level" }, new TagDefinition { Path = "[a]Pump/Flow" } };
            var tree = new DocumentTree(TagTreeBuilder.Build(tags)); Equal("[a]", tree.Roots[0].Label); Equal("Pump", tree.Get("0/0").Label); Equal("[a]Pump/Flow", tree.Get("0/0/0").Data.Text);
            Equal(TagTreeBuilder.Build(tags), TagTreeBuilder.Build(tags.Reverse()));
        });
        Add("tag browse root uses folder boundaries rather than string prefix collisions", () => {
            var tags = new[] { new TagDefinition { Path = "[a]Tank/Level" }, new TagDefinition { Path = "[a]Tank2/Level" } };
            var tree = new DocumentTree(TagTreeBuilder.Build(tags, "[a]Tank")); Equal(3, tree.Items.Count); Equal("[a]Tank/Level", tree.Items.Last().Data.Text);
        });
        Add("tag tree rejects duplicate paths and retains tag-folder collisions", () => {
            var tag = new TagDefinition { Path = "[a]Tank" }; Throws<ArgumentException>(() => TagTreeBuilder.Build([tag, tag]));
            var value = TagTreeBuilder.Build([tag, new() { Path = "[a]Tank/Level" }]); Assert(value.Items[0].Fields["items"].Items[0].Fields["tag"].Boolean);
        });
        Add("tree documents round-trip through project persistence", () => {
            var project = ScadaSpace.Runtime.DemoProject.Create(); project.Views[0].Root.Props["items"] = Source(); Equal(Source(), ProjectCodec.Clone(project).Views[0].Root.Props["items"]);
        });
    }
}
