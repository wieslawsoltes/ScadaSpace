using Microsoft.UI.Xaml.Controls;
using ScadaSpace.Core;

namespace ScadaSpace.App;

public sealed partial class WorkbenchPage
{
    private MenuFlyoutItem? _wrapCoordinateMenu, _sourceViewMenu;
    private string? _sourceViewError;
    private void InitializeHierarchyCommands()
    {
        if (_commands["menu-Component"] is not MenuBarItem menu) return;
        var wrap = new MenuFlyoutSubItem { Text = "Wrap In Container" };
        _wrapCoordinateMenu = new() { Text = "Coordinate Container", IsEnabled = false };
        _wrapCoordinateMenu.Click += (_, _) => WrapSelectedComponents(); wrap.Items.Add(_wrapCoordinateMenu);
        _sourceViewMenu = new() { Text = "Modify Source View", IsEnabled = false };
        _sourceViewMenu.Click += (_, _) => ModifySourceView();
        menu.Items.Insert(0, wrap); menu.Items.Insert(1, _sourceViewMenu); menu.Items.Insert(2, new MenuFlyoutSeparator());
        _commands["wrap-container"] = wrap; _commands["wrap-coordinate"] = _wrapCoordinateMenu; _commands["modify-source"] = _sourceViewMenu;
        _surface.SelectionChanged += UpdateHierarchyCommands;
    }
    private void UpdateHierarchyCommands()
    {
        if (_document is null || _wrapCoordinateMenu is null || _sourceViewMenu is null) return;
        var editing = !_preview && !_sessionMode && _resources.ActiveView is not null;
        // Expensive structural validation is performed when the command runs. The
        // selection can change without a document revision; do not scan it every tick.
        _wrapCoordinateMenu.IsEnabled = editing && _document.Selection.Count > 0;
        _sourceViewMenu.IsEnabled = editing && _document.Selection.Count == 1
            && _document.Find(_document.Selection.Single())?.Type is "EmbeddedView" or "FlexRepeater";
    }
    private void WrapSelectedComponents()
    {
        if (_preview || _sessionMode || _resources.ActiveView is null) return;
        Edit(() =>
        {
            var id = _document.WrapSelectionInCoordinateContainer();
            _surface.RefreshSelection(); SetStatus("Wrapped selection in " + _document.Find(id)!.Name + " · positions and binding targets retained");
        });
        PublishTelemetry();
    }
    private void ModifySourceView()
    {
        if (_preview || _sessionMode || _document.Selection.Count != 1) return;
        var node = _document.Find(_document.Selection.Single());
        if (node is null || node.Type is not ("EmbeddedView" or "FlexRepeater")) return;
        try
        {
            // Use the current evaluated path, not a stale default when props.view is bound.
            var sample = _surface.Host.Read(node, "view");
            if (!sample.IsGood || sample.Value.Kind != ScalarKind.String || string.IsNullOrWhiteSpace(sample.Value.Text))
                throw new InvalidOperationException("Source view binding is not a good non-empty path (" + sample.Quality + ").");
            var path = sample.Value.Text!;
            if (!_document.Project.Views.Any(v => v.Path == path)) throw new InvalidOperationException("Unknown source view: " + path);
            _sourceViewError = null; OpenView(path); SetStatus("Editing source view: " + path);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        { _sourceViewError = error.Message; SetStatus(error.Message); }
        PublishTelemetry();
    }
}
