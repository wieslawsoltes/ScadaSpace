using ScadaSpace.Core;
using ScadaSpace.Data;

namespace ScadaSpace.Controls;

public partial class ViewHost
{
    private HmiTree BuildTree(ComponentNode node)
    {
        var tree = new HmiTree(); Scalar previousSelection = Scalar.Null, previousPaths = Scalar.Null;
        string? tagRoot = null; Scalar tagItems = Scalar.Null;
        _refresh.Add(() =>
        {
            try
            {
                Scalar items;
                if (node.Type == "TagBrowseTree")
                {
                    var root = Text(node, "root");
                    if (root != tagRoot) { var next = TagTreeBuilder.Build(Runtime!.Tags.Browse(), root); tagItems = next; tagRoot = root; }
                    items = tagItems;
                }
                else
                {
                    var sample = Read(node, "items");
                    if (!sample.IsGood) throw new InvalidOperationException("Items quality: " + sample.Quality);
                    items = sample.Value;
                }
                tree.ShowFilter = Flag(node, "showFilter", true); tree.RowHeight = Math.Clamp(Number(node, "rowHeight", 28), 20, 96); tree.MultiSelect = Flag(node, "multiSelect");
                tree.SetItems(items); tree.Filter = Text(node, "filter");
                var paths = Value(node, "selectedPaths", Scalar.Array([])); var selection = Value(node, "selection", Scalar.From(""));
                if (paths != previousPaths || selection != previousSelection)
                {
                    var requested = paths != previousPaths && paths.Kind == ScalarKind.Array && paths.Items.Count > 0
                        ? paths.Items.Select(v => v.Kind == ScalarKind.String ? v.Text! : throw new FormatException("Selected paths must be strings.")).ToArray()
                        : selection.Kind == ScalarKind.String && selection.Text!.Length > 0 ? [selection.Text!] : Array.Empty<string>();
                    tree.SelectPaths(requested); previousPaths = paths; previousSelection = selection;
                }
                tree.SetDataError(null);
                tree.IsEnabled = !IsEditing && Flag(node, "enabled", true) && Flag(node, "interactable", true) && Instance!.CanWrite(node, "selection");
                // Recompute contextual output after data replacement without changing project defaults.
                if (!IsEditing && Read(node, "selectionData").Value != tree.SelectionData) Instance!.Write(node, "selectionData", tree.SelectionData, Session!.Actor);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
            {
                tree.IsEnabled = false;
                if (tree.DataError != ex.Message) Error?.Invoke(ex.Message);
                tree.SetDataError(ex.Message);
            }
        });
        tree.SelectionRequested = change =>
        {
            if (_updating || IsEditing || Instance is null || Session is null) return false;
            var old = Read(node, "selection");
            var result = Instance.Write(node, "selection", Scalar.From(change.CurrentPath), Session.Actor, old.Revision);
            if (!result.Success) { Error?.Invoke(result.Message); return false; }
            previousSelection = Scalar.From(change.CurrentPath); previousPaths = Scalar.Array(change.Paths.Select(Scalar.From));
            Instance.Write(node, "selectedPaths", previousPaths, Session.Actor);
            Instance.Write(node, "selectionData", change.Data, Session.Actor);
            ValueChanged?.Invoke(); return true;
        };
        tree.ItemInvoked += item =>
        {
            if (_updating || IsEditing || Instance is null || Session is null) return;
            Instance.Write(node, "lastEvent", item.EventData, Session.Actor);
            Action(node, "onItemClicked"); ValueChanged?.Invoke();
        };
        tree.FilterChanged += filter => Write(node, "filter", Scalar.From(filter));
        return tree;
    }
}
