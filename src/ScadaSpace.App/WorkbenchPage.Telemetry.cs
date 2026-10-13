using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        // Observe realized native menu items only while the popup is open. Browser tests
        // must wait for this geometry before input; no managed command is exposed here.
        object? ConfigurationKindMenu()
        {
            if (_configurationExplorer?.EditorFields["kind"] is not ComboBox { IsDropDownOpen: true } combo) return null;
            return ((IEnumerable<string>)combo.ItemsSource).Select((name, index) => new
            {
                name, bounds = combo.ContainerFromIndex(index) is FrameworkElement item ? Bounds(item) : null
            }).ToArray();
        }
        object? StyleClassRows()
        {
            if (_styleEditor?.EditorFields["classes"] is not ListView list) return null;
            return ((IEnumerable<string>)list.ItemsSource).Select(name => new
            {
                name, bounds = list.ContainerFromItem(name) is FrameworkElement item ? Bounds(item) : null
            }).ToArray();
        }
        object? StyleInputStates()
        {
            if (_styleEditor is null) return null;
            return _styleEditor.ValueEditors.ToDictionary(pair => pair.Key, pair => new
            {
                text = pair.Value.Text, colorOpen = pair.Value.IsColorFlyoutOpen, colorError = pair.Value.ColorError,
                controls = pair.Value.AuxiliaryControls.ToDictionary(p => p.Key, p => Bounds(p.Value)),
                options = pair.Value.InputElement is ComboBox { IsDropDownOpen: true } combo
                    ? ((IEnumerable<string>)combo.ItemsSource).Select((name, index) => new
                    { name, bounds = combo.ContainerFromIndex(index) is FrameworkElement item ? Bounds(item) : null }).ToArray() : null
            }, StringComparer.Ordinal);
        }
        var controls = _commands.ToDictionary(p => p.Key, p => Bounds(p.Value), StringComparer.Ordinal);
        if (_inspector.EventConfigurationButton is { } eventButton) controls["inspector-configure-events"] = Bounds(eventButton);
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
            components[node.Name] = new { id = node.Id, type = node.Type, x = node.X, y = node.Y, width = node.Width, height = node.Height, bounds = Bounds(element),
                nativeStyle = element is TextBlock textBlock ? new
                {
                    foreground = (textBlock.Foreground as Microsoft.UI.Xaml.Media.SolidColorBrush)?.Color.ToString(),
                    fontSize = textBlock.FontSize, fontWeight = textBlock.FontWeight.Weight,
                    alignment = textBlock.TextAlignment.ToString(), opacity = (textBlock.Parent as Border)?.Opacity ?? textBlock.Opacity
                } : null, parentId = _document.FindParent(node.Id)?.Id, text = node.Props.ContainsKey("text") || node.Bindings.ContainsKey("text") ? ActiveHost.Read(node, "text") : (QualifiedValue?)null, value = node.Props.ContainsKey("value") || node.Bindings.ContainsKey("value") ? ActiveHost.Read(node, "value") : (QualifiedValue?)null };
        }
        BrowserServices.SetTelemetry(new
        {
            styleEditor = _styleEditor is null ? null : new
            {
                fields = _styleEditor.EditorFields.ToDictionary(p => p.Key, p => Bounds(p.Value)),
                selected = _styleEditor.SelectedClass, classes = _styleEditor.Draft.Names, rows = StyleClassRows(), error = _styleEditor.ErrorText,
                inputs = StyleInputStates(), previewVisible = _styleEditor.Preview.Visibility == Visibility.Visible,
                previewFontSize = _styleEditor.PreviewText.FontSize,
                previewFontWeight = _styleEditor.PreviewText.FontWeight.Weight,
                previewFontStyle = _styleEditor.PreviewText.FontStyle.ToString(),
                previewForeground = (_styleEditor.PreviewText.Foreground as Microsoft.UI.Xaml.Media.SolidColorBrush)?.Color.ToString(),
                previewOpacity = _styleEditor.Preview.Opacity
            },
            inspectorFilter = new
            {
                query = _inspector.FilterQuery, boundOnly = _inspector.FilterBoundOnly,
                syntax = _inspector.FilterSyntax.ToString(), mode = _inspector.FilterMatchMode.ToString(),
                caseSensitive = _inspector.FilterCaseSensitive, error = _inspector.FilterError,
                matches = _inspector.MatchingPropertyPaths, categories = _inspector.CategoryStates,
                headers = _inspector.CategoryHeaders.ToDictionary(p => p.Key, p => Bounds(p.Value)),
                patternBuilds = _inspector.FilterPatternBuildCount,
                options = _inspector.IsSearchOptionsOpen ? _inspector.SearchOptions.ToDictionary(p => p.Key, p => new
                { bounds = Bounds(p.Value), selected = p.Value is ToggleMenuFlyoutItem toggle && toggle.IsChecked }) : null,
                visible = _inspector.VisiblePropertyPaths, total = _inspector.PropertyCount,
                noMatches = _inspector.FilterHasNoMatches, summary = _inspector.FilterSummary,
                rowsCreated = _inspector.PropertyRowsCreated, builds = _inspector.FilterBuildCount, applies = _inspector.FilterApplyCount,
                controls = _inspector.FilterCommands.ToDictionary(p => p.Key, p => Bounds(p.Value))
            },
            inspectorQualified = _inspector.QualifiedEditorFields.ToDictionary(p => p.Key, p => Bounds(p.Value)),
            inspectorTyped = TypedInspectorTelemetry(Bounds),
            inspector = _inspector.EditorFields.ToDictionary(p => p.Key, p => Bounds(p.Value)),
            inspectorChoices = _inspector.ChoiceEditors.ToDictionary(p => p.Key, p => new
            {
                enabled = p.Value.IsEnabled, selected = p.Value.SelectedItem as string,
                options = p.Value.IsDropDownOpen ? ((IEnumerable<string>)p.Value.ItemsSource).Select((name, index) => new
                { name, bounds = p.Value.ContainerFromIndex(index) is FrameworkElement item ? Bounds(item) : null }).ToArray() : null
            }),
            hierarchy = new { builds = _document.HierarchyBuildCount, layoutBuilds = _surface.LayoutBuildCount, sourceError = _sourceViewError, restoreErrors = _document.RestoreDiagnostics },
            symbolLibrary = SymbolLibraryTelemetry(Bounds),
            symbolDefaults = _session.Symbols,
            workspace = new { active = _resources.ActiveView, open = _resources.OpenViews, panels = _panels, paletteOpen = _paletteOpen, paletteVisible = _componentPane.Visibility == Visibility.Visible, paletteButtonsCreated = _palette.CreatedButtonCount, paletteVisibleCount = _palette.VisibleComponentCount, tabsCreated = _viewTabs.CreatedTabCount, explorerNodesCreated = _explorerNodesCreated, explorerNodes = _explorerNodes.Count },
            eventEditor = _eventEditor is null ? null : new { fields = _eventEditor.EditorFields.ToDictionary(p => p.Key, p => Bounds(p.Value)), activeEvent = _eventEditor.ActiveEvent, selected = _eventEditor.SelectedAction, count = _eventEditor.ActionCount, error = _eventEditor.ErrorText, dirty = _eventEditor.Draft.IsDirty },
            configurationExplorer = _configurationExplorer is null ? null : new { fields = _configurationExplorer.EditorFields.ToDictionary(p => p.Key, p => Bounds(p.Value)), count = _configurationExplorer.Rows.Count, selected = _configurationExplorer.Selected, kindMenu = ConfigurationKindMenu(), builds = _configurationIndex.BuildCount, queries = _configurationIndex.QueryBuildCount },
            page = PageTelemetry(Bounds),
            pageEditor = _pageEditor?.EditorFields.ToDictionary(p => p.Key, p => Bounds(p.Value)),
            ready = ObserveNativeReadiness(), project = _document.Project.Name, view = _view, preview = _preview, sessionMode = _sessionMode,
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
