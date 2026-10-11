using Microsoft.UI.Xaml;
using ScadaSpace.Core;
using Windows.Foundation;

namespace ScadaSpace.App;

public sealed partial class WorkbenchPage
{
    private void PublishTelemetry()
    {
        if (_document is null || _runtime is null || ActualWidth <= 0) return;
        object? Bounds(FrameworkElement element)
        {
            if (!element.IsLoaded || element.ActualWidth <= 0 || element.ActualHeight <= 0) return null;
            for (DependencyObject? parent = element; parent is not null; parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent))
                if (parent is UIElement { Visibility: Visibility.Collapsed }) return null;
            try { var box = element.TransformToVisual(null).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight)); return new { x = box.X, y = box.Y, width = box.Width, height = box.Height }; } catch (InvalidOperationException) { return null; }
        }
        var controls = _commands.ToDictionary(p => p.Key, p => Bounds(p.Value), StringComparer.Ordinal);
        foreach (var pair in _viewTabs.OpenButtons) controls["view-" + pair.Key] = Bounds(pair.Value);
        foreach (var pair in _viewTabs.CloseButtons) controls["close-view-" + pair.Key] = Bounds(pair.Value);
        foreach (var pair in _viewTabs.ContextMenuItems) controls["tab-menu-" + pair.Key] = Bounds(pair.Value);
        foreach (var (key, menus) in _panelMenus) for (var i = 0; i < menus.Count; i++) controls["panel-menu-"+key+"-"+i] = Bounds(menus[i]);
        controls["palette-search"] = Bounds(_palette.SearchInput);
        foreach (var pair in _palette.Commands) controls["palette-" + pair.Key] = Bounds(pair.Value);
        foreach (var pair in _palette.Items) controls["palette-item-" + pair.Key] = Bounds(pair.Value);
        foreach (var (key, item) in _surface.ContextMenuItems) controls["context-" + key] = Bounds(item);
        foreach (var (key, item) in _configurationButtons) controls["configuration-dialog-" + key] = Bounds(item);
        var components = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var node in ActiveHost.Instance?.View.Root.DescendantsAndSelf() ?? Enumerable.Empty<ComponentNode>())
        {
            if (!ActiveHost.Elements.TryGetValue(node.Id, out var element)) continue;
            components[node.Name] = new { id = node.Id, type = node.Type, x = node.X, y = node.Y, width = node.Width, height = node.Height, bounds = Bounds(element), text = node.Props.ContainsKey("text") || node.Bindings.ContainsKey("text") ? ActiveHost.Read(node, "text") : (QualifiedValue?)null, value = node.Props.ContainsKey("value") || node.Bindings.ContainsKey("value") ? ActiveHost.Read(node, "value") : (QualifiedValue?)null };
        }
        BrowserServices.SetTelemetry(new
        {
            workspace = new { active = _resources.ActiveView, open = _resources.OpenViews, panels = _panels, paletteOpen = _paletteOpen, paletteVisible = _componentPane.Visibility == Visibility.Visible, paletteButtonsCreated = _palette.CreatedButtonCount, paletteVisibleCount = _palette.VisibleComponentCount, tabsCreated = _viewTabs.CreatedTabCount, explorerNodesCreated = _explorerNodesCreated, explorerNodes = _explorerNodes.Count },
            eventEditor = _eventEditor is null ? null : new { fields = _eventEditor.EditorFields.ToDictionary(p => p.Key, p => Bounds(p.Value)), activeEvent = _eventEditor.ActiveEvent, selected = _eventEditor.SelectedAction, count = _eventEditor.ActionCount, error = _eventEditor.ErrorText, dirty = _eventEditor.Draft.IsDirty },
            configurationExplorer = _configurationExplorer is null ? null : new { fields = _configurationExplorer.EditorFields.ToDictionary(p => p.Key, p => Bounds(p.Value)), count = _configurationExplorer.Rows.Count, selected = _configurationExplorer.Selected, builds = _configurationIndex.BuildCount, queries = _configurationIndex.QueryBuildCount },
            page = PageTelemetry(Bounds),
            pageEditor = _pageEditor?.EditorFields.ToDictionary(p => p.Key, p => Bounds(p.Value)),
            ready = true, project = _document.Project.Name, view = _view, preview = _preview, sessionMode = _sessionMode,
            dirty = _document.IsDirty, generation = _document.Generation, canUndo = _document.CanUndo, canRedo = _document.CanRedo,
            zoom = _surface.Zoom, selection = _document.Selection.Select(id => _document.Find(id)?.Name).ToArray(),
            tags = _runtime.Tags.Snapshot(), alarms = _runtime.Alarms.Status(), journalCount = _runtime.Alarms.Journal().Length,
            historyCount = _runtime.History.Count("[default]Plant/Tank01/Level"),
            diagnostics = ActiveHost.Instance?.Diagnostics ?? Array.Empty<Diagnostic>(), errors = _errors, status = _status.Text, controls, components,
            componentCount = _componentCount,
            viewport = new { width = ActualWidth, height = ActualHeight }, tick = _ticks
        });
    }
}
