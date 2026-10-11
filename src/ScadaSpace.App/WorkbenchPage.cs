using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ScadaSpace.Controls;
using ScadaSpace.Core;
using ScadaSpace.Designer;
using ScadaSpace.Runtime;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace ScadaSpace.App;

public sealed partial class WorkbenchPage : Page, IDisposable
{
    private DesignerDocument _document = null!;
    private ProjectRuntime _runtime = null!;
    private PlantSimulator _simulator = null!;
    private RuntimeSession _session = null!;
    private readonly DesignerSurface _surface = new();
    private readonly PropertyInspector _inspector = new();
    private readonly ComponentPalette _palette = new();
    private readonly TreeView _explorer = new() { SelectionMode = TreeViewSelectionMode.Single };
    private readonly HmiDataTable _tags = new();
    private readonly ResourceTabStrip _viewTabs = new();
    private readonly StackPanel _toolbar = new() { Orientation = Orientation.Horizontal, Spacing = 3, Margin = new Thickness(5, 3, 4, 3) };
    private readonly TextBlock _title = ControlTheme.Text("ScadaSpace Designer", 13);
    private readonly TextBlock _status = ControlTheme.Text("Ready", 11);
    private readonly TextBlock _metrics = ControlTheme.Text("", 10);
    private readonly TextBlock _zoomText = ControlTheme.Text("75%", 11);
    private readonly Grid _workspace = new();
    private readonly Grid _left = new();
    private readonly Grid _right = new();
    private readonly Dictionary<string, FrameworkElement> _commands = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly List<string> _errors = [];
    private string _view = "Overview";
    private string _clipboard = "";
    private bool _preview;
    private bool _sessionMode;
    private bool _loading;
    private bool _dialog;
    private int _ticks;
    private double _leftWidth = 235;
    private double _rightWidth = 285;
    private DateTimeOffset _last = DateTimeOffset.UtcNow;
    public WorkbenchPage(bool sessionMode = false)
    {
        Background = ControlTheme.Brush("#D7DADD"); FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Open Sans");
        _sessionMode = sessionMode || BrowserServices.Query("session") == "1";
        _preview = _sessionMode;
        LoadPanelPreferences(); BuildUi();
        _pageHost.Error += SetStatus; _pageHost.ValueChanged += () => { RefreshTags(); PublishTelemetry(); };
        _surface.SelectionChanged += RefreshInspector;
        _surface.Status += SetStatus; _surface.Host.ValueChanged += () => { RefreshTags(); PublishTelemetry(); };
        _surface.ZoomChanged += () => _zoomText.Text = $"{_surface.Zoom * 100:0}%";
        _palette.ComponentRequested += type => { if (!_preview) _surface.AddComponent(type); };
        _inspector.Error += SetStatus;
        _explorer.ItemInvoked += (_, args) =>
        {
            var item = args.InvokedItem is TreeViewNode treeNode ? treeNode.Content as ExplorerItem : args.InvokedItem as ExplorerItem;
            if (item?.Name == "Page Configuration") { RunDialog(EditPagesAsync); return; }
            if (item?.View is not { } view) return;
            if (_view != view) OpenView(view);
            if (item.ComponentId is not null) { _document.Select(item.ComponentId); _surface.RefreshSelection(); }
        };
        _timer.Tick += Tick;
        KeyDown += HandleKeys;
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        Loaded += (_, _) =>
        {
            try
            {
                var saved = BrowserServices.LoadProject();
                InstallProject(string.IsNullOrWhiteSpace(saved) ? DemoProject.Create() : ProjectCodec.Deserialize(saved));
                SetStatus(string.IsNullOrWhiteSpace(saved) ? "Isolated water-treatment simulation · no live equipment connected" : "Loaded locally saved project");
            }
            catch (Exception ex) when (ex is FormatException or JsonException or IOException or InvalidOperationException)
            { InstallProject(DemoProject.Create()); SetStatus("Saved project was not loaded: " + ex.Message); }
            _surface.Fit(); _timer.Start();
        };
        Unloaded += (_, _) => { _timer.Stop(); FlushPanelPreferences(); };
    }
    private void BuildUi() => BuildDesignerUi();
    private void AddMenu(MenuBar menu, string title, params (string Text, Action Action)[] items)
    {
        var group = new MenuBarItem { Title = title, FontSize = 12, MinHeight = 0, Padding = new Thickness(6, 0, 6, 0) };
        foreach (var item in items) { var entry = new MenuFlyoutItem { Text = item.Text }; entry.Click += (_, _) => item.Action(); group.Items.Add(entry); _commands["menu-"+title+"-"+item.Text]=entry; }
        menu.Items.Add(group); _commands["menu-"+title]=group;
    }
    private void AddTool(string title, Action action, string id)
    {
        var glyph = id == "launch-session" ? "session" : id;
        var button = new DesignerToolButton(glyph, title, action, id, id == "preview" || id == "simulation"); _toolbar.Children.Add(button); _commands[id] = button;
    }
    private void Separator() => _toolbar.Children.Add(new Border { Width = 1, Background = ControlTheme.Brush("#B8C6CE"), Margin = new Thickness(4, 2, 4, 2) });
    private void InstallProject(ProjectDefinition project)
    {
        _loading = true;
        if (_document is not null) _document.Changed -= DocumentChanged;
        if (_session is not null) { _session.Changed -= SessionChanged; _session.Navigated -= PageNavigated; }
        _pageHost.Dispose(); _surface.Dispose(); _runtime?.Dispose();
        _document = new(project); _runtime = new(_document.Project); _simulator = new(_runtime.Tags); _session = new(_runtime);
        if (_sessionMode)
        {
            _session.Navigate(BrowserServices.InitialPageUrl(), replace: true);
            _pageHost.Load(_runtime, _session);
            if (_session.Current is { } current) { _view = current.Page.View; BrowserServices.PublishPageNavigation(new(current, NavigationKind.Replace), project.Name); }
        }
        _session.Changed += SessionChanged; _session.Navigated += PageNavigated;
        _document.Changed += DocumentChanged;
        if (!_document.Project.Views.Any(v => v.Path == _view)) _view = _document.Project.Pages.FirstOrDefault()?.View ?? _document.Project.Views[0].Path;
        _resources.Reset(_document.Project.Views.Select(v => v.Path), _view);
        _tagDefinitions = _runtime.Tags.Browse(); _componentCount = _document.Project.Views.Sum(v => v.Root.DescendantsAndSelf().Count());
        _savedExpansion = null; _explorerNodes.Clear(); _explorer.RootNodes.Clear(); BuildExplorer(); BuildTabs(); OpenView(_view, true); RefreshTags(); _loading = false; UpdateButtons(); PublishTelemetry();
    }
    private void DocumentChanged()
    {
        _runtime.UpdateViews(_document.Project);
        _session.Changed -= SessionChanged; _session.Navigated -= PageNavigated;
        _session = new(_runtime); _session.Changed += SessionChanged; _session.Navigated += PageNavigated;
        if (_sessionMode) _pageHost.Load(_runtime, _session);
        _resources.Reconcile(_document.Project.Views.Select(v => v.Path));
        _componentCount = _document.Project.Views.Sum(v => v.Root.DescendantsAndSelf().Count());
        BuildExplorer(); BuildTabs();
        if (_resources.ActiveView is {} active) OpenView(active, true); else CloseWorkspace(); UpdateButtons();
    }
    private void OpenView(string path, bool force = false)
    {
        if (_runtime is null) return;
        if (_sessionMode) { _pageHost.Refresh(); _view = _session.Current?.Page.View ?? path; UpdateButtons(); PublishTelemetry(); return; }
        if (!_document.Project.Views.Any(v => v.Path == path)) { SetStatus("Unknown view: " + path); return; }
        _resources.Open(path); BuildTabs();
        if (!force && _view == path && _surface.Host.Instance is not null && _surface.Host.IsEditing == !_preview) { RevealExplorerView(path); return; }
        _view = path; _emptyWorkspace.Visibility = Visibility.Collapsed; _surface.Visibility = Visibility.Visible;
        _surface.Load(_document, _runtime, path, _preview, _session); RefreshInspector(); UpdateButtons(); RevealExplorerView(path);
    }
    private void BuildTabs()
    {
        if (_sessionMode || _lastTabRevision == _resources.Revision) return;
        _viewTabs.Update(_resources.OpenViews, _resources.ActiveView); _lastTabRevision = _resources.Revision;
    }
    private void RefreshInspector()
    {
        if (_sessionMode || _document is null || _surface.Host.Instance is null) return;
        var id = _document.Selection.Count == 1 ? _document.Selection.Single() : null;
        _inspector.Inspect(_document, id, _surface.Host);
        if (_document.Selection.Count > 1) SetStatus(_document.Selection.Count + " components selected");
        PublishTelemetry();
    }
    private void RefreshTags()
    {
        if (_runtime is null) return;
        _tags.SetData([new("path", "Tag", 2.1), new("value", "Value", .8), new("quality", "Quality", .8)], _tagDefinitions.Select(d => { var sample = _runtime.Tags.Read(d.Path); return new TableRow(d.Path, [d.Path.Replace("[default]Plant/", "", StringComparison.Ordinal), sample.Value + (d.Unit.Length > 0 ? " " + d.Unit : ""), sample.Quality.ToString()], d.Writable ? "#36A0B9" : null); }).ToArray());
    }
    private void Tick(object? sender, object e)
    {
        if (_runtime is null || _loading) return;
        try
        {
            var now = DateTimeOffset.UtcNow; _simulator.Advance(now - _last); _last = now; _runtime.Alarms.Evaluate();
            FlushPanelPreferences(); ReadBrowserNavigation();
            if (_sessionMode) _pageHost.Refresh(); else { _surface.Host.Refresh(); _inspector.RefreshValues(); }
            if (++_ticks % 4 == 0) { RefreshTags(); UpdateButtons(); }
            var imported = BrowserServices.TakeImport();
            if (imported is not null) { var project = ProjectCodec.Deserialize(imported); InstallProject(project); SetStatus("Imported " + project.Name + " · save to retain this project"); }
            PublishTelemetry();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException or IOException or JsonException) { SetStatus(ex.Message); }
    }
    private void UpdateButtons()
    {
        if (_document is null) return;
        UpdateSessionBar();
        ((Button)_commands["page-configuration"]).IsEnabled = !_preview;
        _title.Text = _document.Project.Name + (_document.IsDirty ? " *" : "") + "  —  ScadaSpace";
        ((Button)_commands["undo"]).IsEnabled = !_preview && _document.CanUndo; ((Button)_commands["redo"]).IsEnabled = !_preview && _document.CanRedo;
        var hasView = _sessionMode || _resources.ActiveView is not null;
        var previewButton = (DesignerToolButton)_commands["preview"]; previewButton.SetGlyph(_preview ? "design" : "preview"); previewButton.SetLabel(_preview ? "Design (F5)" : "Preview (F5)"); previewButton.IsEnabled = !_sessionMode && hasView;
        var simulationButton = (DesignerToolButton)_commands["simulation"]; simulationButton.SetGlyph(_simulator.Running ? "simulation" : "play"); simulationButton.SetLabel(_simulator.Running ? "Pause simulation" : "Resume simulation");
        foreach (var id in new[] { "cut", "copy", "paste", "delete", "front", "back", "align-left", "align-top" }) ((Button)_commands[id]).IsEnabled = !_preview && hasView;
        _palette.IsHitTestVisible = !_preview; _palette.Opacity = _preview ? .55 : 1; _inspector.IsHitTestVisible = !_preview;
        _metrics.Text = $"{_tagDefinitions.Count} tags   {_runtime.Alarms.Status().Count(a => a.Active)} active alarms   Uno Skia   {_surface.Zoom * 100:0}%";
    }
    private void UpdateResponsiveLayout() => ArrangeDesignerPanels();
    private void SetStatus(string text) => _status.Text = text;
    public void ReportUnhandled(string message) { _errors.Add(message); while (_errors.Count > 20) _errors.RemoveAt(0); SetStatus(message); PublishTelemetry(); }
    public new void Dispose() { _timer.Stop(); FlushPanelPreferences(); if (_session is not null) { _session.Changed -= SessionChanged; _session.Navigated -= PageNavigated; } _pageHost.Dispose(); _surface.Dispose(); _runtime?.Dispose(); }
    private sealed record ExplorerItem(string Name, string? View = null, string? ComponentId = null) { public override string ToString() => Name; }
}
