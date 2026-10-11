using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using ScadaSpace.Data;
using static Suite;

internal static class TreeHardeningTests
{
    [ModuleInitializer]
    internal static void Register()
    {
        Add("oversized tree selection output is rejected atomically", () => {
            var tree = new DocumentTree(Scalar.Array(Enumerable.Repeat(Scalar.Object([]), 3000))); var state = new DocumentTreeState(tree); state.Select(["0"]);
            var before = state.SelectionData;
            Throws<ArgumentException>(() => state.Select(tree.Items.Select(n => n.Path)));
            Equal("0", state.CurrentPath); Equal(before, state.SelectionData); Equal(1, state.Selected.Count);
        });
        Add("repeated tree selection enumeration is bounded", () => {
            var state = new DocumentTreeState(new(Scalar.FromJson("[{}]")));
            Throws<ArgumentException>(() => state.Select(Enumerable.Repeat("0", DocumentTree.MaximumItems + 1))); Equal("", state.CurrentPath);
        });
        Add("tree replacement refreshes selected data without mutating previous snapshots", () => {
            var source = Scalar.FromJson("[{\"id\":\"item\",\"data\":1}]"); var state = new DocumentTreeState(new(source)); state.Select(["0"]); var before = state.SelectionData;
            state.Replace(new(PropertyPath.Parse("[0].data").Set(source, Scalar.From(9)))); Equal(9d, state.SelectionData.Items[0].Fields["value"].Number); Equal(1d, before.Items[0].Fields["value"].Number);
        });
        Add("removing an active selected tree item retains another surviving selection", () => {
            var source = Scalar.FromJson("[{\"id\":\"a\"},{\"id\":\"b\"}]"); var state = new DocumentTreeState(new(source)); state.Select(["0", "1"]);
            state.Replace(new(Scalar.Array([source.Items[0]]))); Equal("0", state.CurrentPath); Equal(1, state.Selected.Count);
        });
    }
}
