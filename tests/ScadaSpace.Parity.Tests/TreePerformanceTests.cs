using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using ScadaSpace.Data;
using static Suite;

internal static class TreePerformanceTests
{
    private static DocumentTree Tree() => new(Scalar.FromJson("""
        [{"id":"root","label":"Plant","expanded":true,"items":[
          {"id":"pump","label":"Pump","items":[{"id":"motor","label":"Motor","data":7}]},
          {"id":"tank","label":"Tank"}]}]
        """));
    [ModuleInitializer]
    internal static void Register()
    {
        Add("tree projection cache is stable across idle reads and selection changes", () => {
            var state = new DocumentTreeState(Tree()); var rows = state.Visible();
            state.Select(["0/1"]);
            for (var i = 0; i < 2000; i++) Assert(ReferenceEquals(rows, state.Visible()));
            Equal(1L, state.ProjectionBuildCount); Equal("0/1", state.CurrentPath);
        });
        Add("tree selected snapshots are immutable and reused without scanning", () => {
            var state = new DocumentTreeState(Tree()); state.Select(["0/1", "0/0"]); var selected = state.Selected; var data = state.SelectionData;
            Equal("0/0", selected[0].Path); Equal("0/1", selected[1].Path);
            for (var i = 0; i < 2000; i++) Assert(ReferenceEquals(selected, state.Selected));
            state.Select(["0/0", "0/1"]); Assert(ReferenceEquals(selected, state.Selected)); Equal(data, state.SelectionData);
            Throws<NotSupportedException>(() => ((IList<DocumentTreeItem>)selected).Clear());
            state.Select([]); Equal(2, selected.Count); Equal(0, state.Selected.Count);
        });
        Add("tree filter clearing restores locally expanded branches", () => {
            var state = new DocumentTreeState(Tree()); state.SetExpanded("0/0", true); state.Select(["0/0", "0/1"]);
            Equal(4, state.Visible().Count); Equal(3, state.Visible("Motor").Count);
            Equal(4, state.Visible("").Count); Equal(2, state.Selected.Count);
            state.SetExpanded("0/0", false); Equal(3, state.Visible().Count);
            Equal(3, state.Visible("Motor").Count); Equal(3, state.Visible().Count);
            Assert(!state.IsExpanded(state.Tree.Get("0/0")));
        });
        Add("tree no-op expansion preserves the cached projection", () => {
            var state = new DocumentTreeState(Tree()); var rows = state.Visible();
            state.SetExpanded("0", true); state.SetExpanded("0/0", false); state.SetExpanded("0/1", true); state.Reveal("0/1");
            Assert(ReferenceEquals(rows, state.Visible())); Equal(1L, state.ProjectionBuildCount);
        });
        Add("recursive tree expansion visits the chosen subtree only", () => {
            var state = new DocumentTreeState(Tree()); state.SetExpanded("0/0", true, true); Equal(4, state.Visible().Count);
            state.ExpandAll(false); Equal(1, state.Visible().Count); state.ExpandAll(true); Equal(4, state.Visible().Count);
            state.SetExpanded("0/0", false, true); Equal(3, state.Visible().Count);
        });
        Add("tree replacement invalidates projection and refreshes selected data", () => {
            var state = new DocumentTreeState(Tree()); state.Select(["0/0/0"]); var rows = state.Visible(); var selected = state.Selected;
            state.Replace(new(Scalar.FromJson("""[{"id":"motor","label":"Renamed","data":9}]""")));
            Assert(!ReferenceEquals(rows, state.Visible())); Equal("0", state.CurrentPath); Equal(9d, state.Selected[0].Data.Number); Equal(7d, selected[0].Data.Number);
        });
        Add("failed selection leaves cached output and tree expansion untouched", () => {
            var state = new DocumentTreeState(Tree()); state.Select(["0/0"]); var selected = state.Selected; var rows = state.Visible(); var data = state.SelectionData;
            Throws<ArgumentException>(() => state.Select(["0/1", "missing"]));
            Assert(ReferenceEquals(selected, state.Selected)); Assert(ReferenceEquals(rows, state.Visible())); Equal(data, state.SelectionData);
        });
    }
}
