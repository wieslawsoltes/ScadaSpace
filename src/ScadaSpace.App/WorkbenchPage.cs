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
    private readonly StackPanel _viewTabs = new() { Orientation = Orientation.Horizontal, Spacing = 1 };
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
    public WorkbenchPage()
    {
        Background = ControlTheme.Brush("#E7ECEF"); FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Open Sans");
        _sessionMode = BrowserServices.Query("session") == "1";
        _preview = _sessionMode;
        BuildUi();
        _surface.SelectionChanged += RefreshInspector;
        _surface.Status += SetStatus; _surface.Host.ValueChanged += () => { RefreshTags(); PublishTelemetry(); };
        _surface.ZoomChanged += () => _zoomText.Text = $"{_surface.Zoom * 100:0}%";
        _palette.ComponentRequested += type => { if (!_preview) _surface.AddComponent(type); };
        _inspector.Error += SetStatus;
        _explorer.ItemInvoked += (_, args) =>
        {
            var item = args.InvokedItem is TreeViewNode treeNode ? treeNode.Content as ExplorerItem : args.InvokedItem as ExplorerItem;
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
        Unloaded += (_, _) => _timer.Stop();
    }
    private void BuildUi()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new() { Height = new GridLength(30) }); root.RowDefinitions.Add(new() { Height = new GridLength(35) }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = new GridLength(26) });
        var top = new Grid { Background = ControlTheme.Brush("#EEF1F3"), BorderBrush = ControlTheme.Brush("#BDC8CF"), BorderThickness = new Thickness(0, 0, 0, 1) };
        top.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); top.ColumnDefinitions.Add(new()); top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var menu = new MenuBar { VerticalAlignment = VerticalAlignment.Center };
        AddMenu(menu, "File", ("New project…", () => RunDialog(NewProjectAsync)), ("Open project file…", () => RunDialog(ImportAsync)), ("Save project", Save), ("Export project JSON", Export), ("Restore demonstration…", () => RunDialog(RestoreDemoAsync)));
        AddMenu(menu, "Edit", ("Undo", Undo), ("Redo", Redo), ("Cut", Cut), ("Copy", Copy), ("Paste", Paste), ("Duplicate", Duplicate), ("Delete", Delete));
        AddMenu(menu, "View", ("Preview / design", TogglePreview), ("Fit view", () => _surface.Fit()), ("Actual size", () => _surface.Zoom = 1), ("Toggle grid", () => _surface.ShowGrid = !_surface.ShowGrid), ("Reset panel layout", () => { _leftWidth = 235; _rightWidth = 285; UpdateResponsiveLayout(); }));
        AddMenu(menu, "Project", ("New view…", () => RunDialog(NewViewAsync)), ("Edit project JSON…", () => RunDialog(EditProjectJsonAsync)), ("Validate project", ValidateProject), ("Export tag history CSV", ExportHistory), ("Launch isolated session", LaunchSession));
        AddMenu(menu, "Help", ("About / compatibility", () => RunDialog(AboutAsync)));
        top.Children.Add(menu); _title.HorizontalAlignment = HorizontalAlignment.Right; _title.Margin = new Thickness(8, 0, 12, 0); Grid.SetColumn(_title, 1); top.Children.Add(_title);
        var badge = new Border { Background = ControlTheme.Brush("#DCEBE5"), Margin = new Thickness(0, 4, 7, 4), Padding = new Thickness(7, 0, 7, 0), Child = ControlTheme.Text("SIMULATION", 10, "#3B7364") }; Grid.SetColumn(badge, 2); top.Children.Add(badge); root.Children.Add(top);
        var toolScroll = new ScrollViewer { Content = _toolbar, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AddTool("Save", Save, "save"); AddTool("Undo", Undo, "undo"); AddTool("Redo", Redo, "redo"); Separator();
        AddTool("Copy", Copy, "copy"); AddTool("Paste", Paste, "paste"); AddTool("Delete", Delete, "delete"); Separator();
        AddTool("◀ Align", () => Edit(() => _document.Align(Alignment.Left)), "align-left"); AddTool("▲ Align", () => Edit(() => _document.Align(Alignment.Top)), "align-top");
        AddTool("To front", () => Edit(() => _document.BringToFront(true)), "front"); Separator();
        AddTool("▶ Preview", TogglePreview, "preview"); AddTool("Fit", () => _surface.Fit(), "fit"); AddTool("−", () => _surface.Zoom /= 1.15, "zoom-out"); _toolbar.Children.Add(_zoomText); AddTool("+", () => _surface.Zoom *= 1.15, "zoom-in"); Separator();
        AddTool("Pause simulation", () => { _simulator.Running = !_simulator.Running; UpdateButtons(); }, "simulation");
        AddTool("Export", Export, "export"); AddTool("Import", () => RunDialog(ImportAsync), "import");
        Grid.SetRow(toolScroll, 1); root.Children.Add(toolScroll);
        _workspace.ColumnDefinitions.Add(new() { Width = new GridLength(_leftWidth) }); _workspace.ColumnDefinitions.Add(new() { Width = new GridLength(4) }); _workspace.ColumnDefinitions.Add(new()); _workspace.ColumnDefinitions.Add(new() { Width = new GridLength(4) }); _workspace.ColumnDefinitions.Add(new() { Width = new GridLength(_rightWidth) });
        _left.RowDefinitions.Add(new() { Height = new GridLength(1.2, GridUnitType.Star) }); _left.RowDefinitions.Add(new() { Height = new GridLength(4) }); _left.RowDefinitions.Add(new());
        _left.Children.Add(new DockPane("Project Browser", _explorer));
        var splitLeft = new DockSplitter { Vertical = true }; splitLeft.DragDelta += delta => { var topHeight = Math.Clamp(_left.RowDefinitions[0].ActualHeight + delta, 100, Math.Max(100, _left.ActualHeight - 130)); _left.RowDefinitions[0].Height = new GridLength(topHeight); }; Grid.SetRow(splitLeft, 1); _left.Children.Add(splitLeft);
        _tags.AddCommand("Write…", _ => RunDialog(WriteTagAsync));
        var tagPane = new DockPane("Tag Browser  ·  [default]", _tags); Grid.SetRow(tagPane, 2); _left.Children.Add(tagPane); _workspace.Children.Add(_left);
        var leftSplit = new DockSplitter(); leftSplit.DragDelta += delta => { _leftWidth = Math.Clamp(_leftWidth + delta, 180, 450); UpdateResponsiveLayout(); }; Grid.SetColumn(leftSplit, 1); _workspace.Children.Add(leftSplit);
        var middle = new Grid(); middle.RowDefinitions.Add(new() { Height = new GridLength(30) }); middle.RowDefinitions.Add(new());
        var tabs = new ScrollViewer { Content = _viewTabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = ControlTheme.Brush("#CBD5DC") }; middle.Children.Add(tabs); Grid.SetRow(_surface, 1); middle.Children.Add(_surface); Grid.SetColumn(middle, 2); _workspace.Children.Add(middle);
        var rightSplit = new DockSplitter(); rightSplit.DragDelta += delta => { _rightWidth = Math.Clamp(_rightWidth - delta, 220, 500); UpdateResponsiveLayout(); }; Grid.SetColumn(rightSplit, 3); _workspace.Children.Add(rightSplit);
        _right.RowDefinitions.Add(new() { Height = new GridLength(.8, GridUnitType.Star) }); _right.RowDefinitions.Add(new() { Height = new GridLength(4) }); _right.RowDefinitions.Add(new() { Height = new GridLength(1.4, GridUnitType.Star) });
        _right.Children.Add(new DockPane("Perspective-style Components", _palette));
        var splitRight = new DockSplitter { Vertical = true }; splitRight.DragDelta += delta => _right.RowDefinitions[0].Height = new GridLength(Math.Clamp(_right.RowDefinitions[0].ActualHeight + delta, 90, Math.Max(90, _right.ActualHeight - 150))); Grid.SetRow(splitRight, 1); _right.Children.Add(splitRight);
        var inspectorPane = new DockPane("Property Editor", _inspector); Grid.SetRow(inspectorPane, 2); _right.Children.Add(inspectorPane); Grid.SetColumn(_right, 4); _workspace.Children.Add(_right);
        Grid.SetRow(_workspace, 2); root.Children.Add(_workspace);
        var status = new Grid { Background = ControlTheme.Brush("#E1E8EC"), Padding = new Thickness(7, 0, 7, 0), BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = ControlTheme.Brush("#B6C5CF") };
        status.ColumnDefinitions.Add(new()); status.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); status.Children.Add(_status); Grid.SetColumn(_metrics, 1); status.Children.Add(_metrics); Grid.SetRow(status, 3); root.Children.Add(status);
        Content = root;
    }
    private void AddMenu(MenuBar menu, string title, params (string Text, Action Action)[] items)
    {
        var group = new MenuBarItem { Title = title };
        foreach (var item in items) { var entry = new MenuFlyoutItem { Text = item.Text }; entry.Click += (_, _) => item.Action(); group.Items.Add(entry); }
        menu.Items.Add(group);
    }
    private void AddTool(string title, Action action, string id)
    {
        var button = ControlTheme.Button(title, action, id); button.Height = 28; _toolbar.Children.Add(button); _commands[id] = button;
    }
    private void Separator() => _toolbar.Children.Add(new Border { Width = 1, Background = ControlTheme.Brush("#B8C6CE"), Margin = new Thickness(4, 2, 4, 2) });
    private void InstallProject(ProjectDefinition project)
    {
        _loading = true;
        if (_document is not null) _document.Changed -= DocumentChanged;
        _runtime?.Dispose();
        _document = new(project); _runtime = new(_document.Project); _simulator = new(_runtime.Tags); _session = new(_runtime);
        _session.Changed += SessionChanged;
        _document.Changed += DocumentChanged;
        if (!_document.Project.Views.Any(v => v.Path == _view)) _view = _document.Project.Pages.FirstOrDefault()?.View ?? _document.Project.Views[0].Path;
        BuildExplorer(); BuildTabs(); OpenView(_view); RefreshTags(); _loading = false; UpdateButtons(); PublishTelemetry();
    }
    private void DocumentChanged()
    {
        _runtime.UpdateViews(_document.Project); BuildExplorer(); BuildTabs();
        if (!_document.Project.Views.Any(v => v.Path == _view)) _view = _document.Project.Views[0].Path;
        OpenView(_view); UpdateButtons();
    }
    private void OpenView(string path)
    {
        if (_runtime is null) return;
        _view = path; _surface.Load(_document, _runtime, path, _preview, _session); RefreshInspector(); BuildTabs(); UpdateButtons();
        foreach (var button in _viewTabs.Children.OfType<Button>()) button.Background = ControlTheme.Brush((string?)button.Tag == path ? "#F7FAFC" : "#D4DFE6");
    }
    private void BuildTabs()
    {
        _viewTabs.Children.Clear();
        var views = _sessionMode ? _document.Project.Pages.Select(p => (p.View, p.Title)).ToArray() : _document.Project.Views.Select(v => (v.Path, v.Path)).ToArray();
        foreach (var (path, title) in views)
        {
            var button = ControlTheme.Button("▣  " + title, () => OpenView(path), "view-" + path);
            button.Tag = path; button.Height = 29; button.Padding = new Thickness(10, 3, 10, 3); button.BorderThickness = new Thickness(0, 0, 1, 0); button.Background = ControlTheme.Brush(path == _view ? "#F7FAFC" : "#D4DFE6");
            _viewTabs.Children.Add(button);
        }
    }
    private void BuildExplorer()
    {
        _explorer.RootNodes.Clear();
        var project = new TreeViewNode { Content = new ExplorerItem(_document.Project.Name), IsExpanded = true };
        var perspective = new TreeViewNode { Content = new ExplorerItem("Perspective"), IsExpanded = true };
        var views = new TreeViewNode { Content = new ExplorerItem("Views"), IsExpanded = true };
        foreach (var view in _document.Project.Views)
        {
            var viewNode = new TreeViewNode { Content = new ExplorerItem(view.Path, view.Path), IsExpanded = view.Path == _view };
            void Add(ComponentNode component, TreeViewNode parent, int depth)
            {
                var node = new TreeViewNode { Content = new ExplorerItem(component.Name + (component.Bindings.Count > 0 ? "  ⛓" : ""), view.Path, component.Id), IsExpanded = depth == 0 && view.Path == _view };
                parent.Children.Add(node); foreach (var child in component.Children) Add(child, node, depth + 1);
            }
            Add(view.Root, viewNode, 0); views.Children.Add(viewNode);
        }
        perspective.Children.Add(views);
        var pages = new TreeViewNode { Content = new ExplorerItem("Page Configuration") }; foreach (var page in _document.Project.Pages) pages.Children.Add(new() { Content = new ExplorerItem(page.Path + " → " + page.View, page.View) }); perspective.Children.Add(pages);
        var styles = new TreeViewNode { Content = new ExplorerItem("Styles") }; foreach (var name in _document.Project.Styles.Keys) styles.Children.Add(new() { Content = new ExplorerItem(name) }); perspective.Children.Add(styles);
        project.Children.Add(perspective); project.Children.Add(new() { Content = new ExplorerItem("Alarms  (" + _document.Project.Alarms.Count + ")", "Alarms") }); _explorer.RootNodes.Add(project);
    }
    private void RefreshInspector()
    {
        if (_document is null || _surface.Host.Instance is null) return;
        var id = _document.Selection.Count == 1 ? _document.Selection.Single() : null;
        _inspector.Inspect(_document, id, _surface.Host);
        if (_document.Selection.Count > 1) SetStatus(_document.Selection.Count + " components selected");
        PublishTelemetry();
    }
    private void RefreshTags()
    {
        if (_runtime is null) return;
        _tags.SetData([new("path", "Tag", 2.1), new("value", "Value", .8), new("quality", "Quality", .8)], _runtime.Tags.Browse().Select(d => { var sample = _runtime.Tags.Read(d.Path); return new TableRow(d.Path, [d.Path.Replace("[default]Plant/", "", StringComparison.Ordinal), sample.Value + (d.Unit.Length > 0 ? " " + d.Unit : ""), sample.Quality.ToString()], d.Writable ? "#36A0B9" : null); }).ToArray());
    }
    private void Tick(object? sender, object e)
    {
        if (_runtime is null || _loading) return;
        try
        {
            var now = DateTimeOffset.UtcNow; _simulator.Advance(now - _last); _last = now; _runtime.Alarms.Evaluate();
            _surface.Host.Refresh(); _inspector.RefreshValues();
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
        _title.Text = _document.Project.Name + (_document.IsDirty ? " *" : "") + "  —  ScadaSpace";
        ((Button)_commands["undo"]).IsEnabled = !_preview && _document.CanUndo; ((Button)_commands["redo"]).IsEnabled = !_preview && _document.CanRedo;
        ((Button)_commands["preview"]).Content = _preview ? "■ Design" : "▶ Preview";
        ((Button)_commands["simulation"]).Content = _simulator.Running ? "Pause simulation" : "Resume simulation";
        _palette.IsHitTestVisible = !_preview; _palette.Opacity = _preview ? .55 : 1; _inspector.IsHitTestVisible = !_preview;
        _metrics.Text = $"{_runtime.Tags.Browse().Count} tags   {_runtime.Alarms.Status().Count(a => a.Active)} active alarms   Uno Skia   {_surface.Zoom * 100:0}%";
    }
    private void UpdateResponsiveLayout()
    {
        var showLeft = !_sessionMode && ActualWidth >= 1050; var showRight = !_sessionMode && ActualWidth >= 850;
        _workspace.ColumnDefinitions[0].Width = new GridLength(showLeft ? _leftWidth : 0); _workspace.ColumnDefinitions[1].Width = new GridLength(showLeft ? 4 : 0);
        _workspace.ColumnDefinitions[4].Width = new GridLength(showRight ? _rightWidth : 0); _workspace.ColumnDefinitions[3].Width = new GridLength(showRight ? 4 : 0);
        _left.Visibility = showLeft ? Visibility.Visible : Visibility.Collapsed; _right.Visibility = showRight ? Visibility.Visible : Visibility.Collapsed;
        if (_sessionMode) _surface.Fit();
    }
    private void TogglePreview() { if (_sessionMode) return; _preview = !_preview; OpenView(_view); SetStatus(_preview ? "Preview mode · controls write only to the isolated simulator" : "Design mode · select and edit components"); }
    private void Edit(Action action) { if (_preview) return; try { action(); } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or JsonException) { SetStatus(ex.Message); } }
    private void Undo() => Edit(() => _document.Undo());
    private void Redo() => Edit(() => _document.Redo());
    private void Copy() { if (_document is null) return; _clipboard = _document.CopySelection(); SetStatus("Copied " + _document.Selection.Count + " components"); }
    private void Cut() { Copy(); Delete(); }
    private void Paste() => Edit(() => { if (_clipboard.Length > 0) _document.Paste(_clipboard, _document.Project.Views.Single(v => v.Path == _view).Root.Id); });
    private void Duplicate() { if (_preview) return; Copy(); Paste(); }
    private void Delete() => Edit(() => _document.Delete());
    private void Save()
    {
        try { BrowserServices.SaveProject(ProjectCodec.Serialize(_document.Project)); _document.MarkSaved(); UpdateButtons(); SetStatus("Project saved locally"); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or FormatException) { SetStatus("Save failed: " + ex.Message); }
    }
    private void Export() { if (_document is not null) SetStatus(BrowserServices.Download("scadaspace-project.json", ProjectCodec.Serialize(_document.Project))); }
    private void ExportHistory()
    {
        var path = _tags.SelectedKey ?? _runtime.Tags.Browse().FirstOrDefault()?.Path;
        if (path is not null) SetStatus(BrowserServices.Download("scadaspace-history.csv", _runtime.History.ExportCsv(path), "text/csv;charset=utf-8"));
    }
    private void ValidateProject()
    {
        var errors = ProjectCodec.Validate(_document.Project); _surface.Host.Instance?.Evaluate();
        var bindings = _surface.Host.Instance?.Diagnostics.Count ?? 0;
        SetStatus(errors.Count == 0 && bindings == 0 ? "Project schema and current view bindings are valid" : $"{errors.Count} project errors, {bindings} current-view binding errors");
    }
    private async Task ImportAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        if (BrowserServices.BeginFileImport()) return;
        await _inspector.EditJsonAsync("Import ScadaSpace project", "", text => { InstallProject(ProjectCodec.Deserialize(text)); SetStatus("Imported project"); }, "Paste an exported ScadaSpace project. Ignition project archives are not compatible.");
    }
    private async Task EditProjectJsonAsync() => await _inspector.EditJsonAsync("Project JSON", ProjectCodec.Serialize(_document.Project), text => { var project = ProjectCodec.Deserialize(text); InstallProject(project); SetStatus("Applied project JSON"); });
    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!_document.IsDirty) return true;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Unsaved project changes", Content = "Replacing the project will discard unsaved design changes. Runtime tag values and history are not project files.", PrimaryButtonText = "Replace project", CloseButtonText = "Cancel" };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
    private async Task NewProjectAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        InstallProject(new ProjectDefinition { Name = "New Project", Views = [DemoProject.View("Main", 1040, 760)], Pages = [new() { Path = "/", View = "Main", Title = "Main" }] }); SetStatus("Created an empty project");
    }
    private async Task RestoreDemoAsync() { if (await ConfirmDiscardAsync()) { InstallProject(DemoProject.Create()); _surface.Fit(); SetStatus("Restored isolated water-treatment demonstration"); } }
    private async Task NewViewAsync()
    {
        if (_preview) return;
        var name = new TextBox { Header = "View path", Text = "NewView", MinWidth = 340 };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = ControlTheme.Brush("#AD4F35") };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "New view", Content = new StackPanel { Spacing = 10, Children = { name, error } }, PrimaryButtonText = "Create", CloseButtonText = "Cancel" };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name.Text) || name.Text.Length > 128 || name.Text.Any(c => !(char.IsLetterOrDigit(c) || c is '_' or '-' or '/'))) throw new FormatException("Use letters, numbers, slash, hyphen and underscore.");
                _document.Execute("Add view", p => p.Views.Add(DemoProject.View(name.Text, 1040, 760))); OpenView(name.Text);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException) { args.Cancel = true; error.Text = ex.Message; }
        };
        await dialog.ShowAsync();
    }
    private async Task WriteTagAsync()
    {
        var path = _tags.SelectedKey; if (path is null) return;
        var definition = _runtime.Tags.Browse().First(d => d.Path == path); var old = _runtime.Tags.Read(path);
        var value = new TextBox { Header = definition.DataType + " value" + (definition.Unit.Length > 0 ? " (" + definition.Unit + ")" : ""), Text = old.Value.ToString(), MinWidth = 350 };
        var error = new TextBlock { Foreground = ControlTheme.Brush("#AD4F35"), TextWrapping = TextWrapping.Wrap };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = path, Content = new StackPanel { Spacing = 8, Children = { value, new TextBlock { Text = definition.Writable ? "Writes apply to the local simulator only. Concurrent value changes are rejected." : "This tag is read-only.", TextWrapping = TextWrapping.Wrap, FontSize = 12 }, error } }, PrimaryButtonText = definition.Writable ? "Write" : "", CloseButtonText = "Cancel" };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                var result = _runtime.Tags.Write(path, Scalar.Parse(value.Text, definition.DataType), "demo-operator", old.Revision);
                if (!result.Success) throw new InvalidOperationException(result.Message);
                RefreshTags(); _surface.Host.Refresh(); SetStatus("Wrote " + path);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException) { args.Cancel = true; error.Text = ex.Message; }
        };
        await dialog.ShowAsync();
    }
    private async Task AboutAsync()
    {
        await new ContentDialog { XamlRoot = XamlRoot, Title = "ScadaSpace 0.1.0", CloseButtonText = "Close", Content = new TextBlock { Text = "Independent C# / Uno Platform SCADA workbench.\n\nReusable project, tag, expression, binding, alarm, history, designer, layout, graphics and control packages.\n\nThis is an isolated simulation, not production industrial-control software. It is not Ignition Perspective and does not have full Perspective compatibility. No proprietary code or visual assets are included.\n\nNot implemented: Ignition Gateway compatibility, Jython, real OPC-UA/device drivers, enterprise authentication, SQL named queries, redundancy and the full Perspective component/API catalog.\n\nGitHub: wieslawsoltes/ScadaSpace", TextWrapping = TextWrapping.Wrap, MaxWidth = 430, FontSize = 13 } }.ShowAsync();
    }
    private void LaunchSession()
    {
        Save();
#if __WASM__
        BrowserServices.OpenSession();
#else
        var window = new Window { Title = "ScadaSpace · isolated session" };
        var page = new WorkbenchPage(); window.Content = page; window.Closed += (_, _) => page.Dispose(); window.Activate();
#endif
    }
    private void SessionChanged()
    {
        var page = _runtime.Project.Pages.FirstOrDefault(p => p.Path == _session.CurrentPath);
        if (page is not null && page.View != _view) OpenView(page.View);
        if (_session.Popups.LastOrDefault() is { } popup && !_dialog) RunDialog(async () =>
        {
            var host = new ViewHost(); host.Load(_runtime, popup.View, popup.Parameters, session: _session);
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = popup.View, Content = new Viewbox { Child = host, MaxWidth = 600, MaxHeight = 500 }, CloseButtonText = "Close" };
            await dialog.ShowAsync(); host.Dispose(); _session.ClosePopup(popup.Id);
        });
    }
    private async void RunDialog(Func<Task> action)
    {
        if (_dialog) return; _dialog = true;
        try { await action(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or FormatException or JsonException) { SetStatus(ex.Message); }
        finally { _dialog = false; }
    }
    private void HandleKeys(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0;
        if (e.Key == VirtualKey.F5) { TogglePreview(); e.Handled = true; return; }
        if (ctrl && e.Key == VirtualKey.S) { Save(); e.Handled = true; return; }
        if (e.OriginalSource is TextBox or NumberBox || _dialog || _preview) return;
        if (ctrl)
        {
            switch (e.Key) { case VirtualKey.Z: Undo(); break; case VirtualKey.Y: Redo(); break; case VirtualKey.C: Copy(); break; case VirtualKey.X: Cut(); break; case VirtualKey.V: Paste(); break; case VirtualKey.D: Duplicate(); break; default: return; }
            e.Handled = true;
        }
        else
        {
            var step = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0 ? 8 : 1;
            switch (e.Key) { case VirtualKey.Delete: Delete(); break; case VirtualKey.Left: Edit(() => _document.Move(-step, 0)); break; case VirtualKey.Right: Edit(() => _document.Move(step, 0)); break; case VirtualKey.Up: Edit(() => _document.Move(0, -step)); break; case VirtualKey.Down: Edit(() => _document.Move(0, step)); break; default: return; }
            e.Handled = true;
        }
    }
    private void SetStatus(string text) => _status.Text = text;
    public void ReportUnhandled(string message) { _errors.Add(message); while (_errors.Count > 20) _errors.RemoveAt(0); SetStatus(message); PublishTelemetry(); }
    private void PublishTelemetry()
    {
        if (_document is null || _runtime is null || _surface.Host.Instance is null || ActualWidth <= 0) return;
        object? Bounds(FrameworkElement element)
        {
            if (!element.IsLoaded || element.ActualWidth <= 0 || element.ActualHeight <= 0) return null;
            try { var box = element.TransformToVisual(null).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight)); return new { x = box.X, y = box.Y, width = box.Width, height = box.Height }; } catch (InvalidOperationException) { return null; }
        }
        var controls = _commands.ToDictionary(p => p.Key, p => Bounds(p.Value), StringComparer.Ordinal);
        foreach (var button in _viewTabs.Children.OfType<Button>()) controls["view-" + button.Tag] = Bounds(button);
        var components = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var node in _surface.Host.Instance.View.Root.DescendantsAndSelf())
        {
            if (!_surface.Host.Elements.TryGetValue(node.Id, out var element)) continue;
            components[node.Name] = new { id = node.Id, type = node.Type, x = node.X, y = node.Y, width = node.Width, height = node.Height, bounds = Bounds(element), value = node.Props.ContainsKey("value") || node.Bindings.ContainsKey("value") ? _surface.Host.Read(node, "value") : (QualifiedValue?)null };
        }
        BrowserServices.SetTelemetry(new
        {
            ready = true, project = _document.Project.Name, view = _view, preview = _preview, sessionMode = _sessionMode,
            dirty = _document.IsDirty, generation = _document.Generation, canUndo = _document.CanUndo, canRedo = _document.CanRedo,
            zoom = _surface.Zoom, selection = _document.Selection.Select(id => _document.Find(id)?.Name).ToArray(),
            tags = _runtime.Tags.Snapshot(), alarms = _runtime.Alarms.Status(), journalCount = _runtime.Alarms.Journal().Length,
            historyCount = _runtime.History.Query("[default]Plant/Tank01/Level").Length,
            diagnostics = _surface.Host.Instance.Diagnostics, errors = _errors, status = _status.Text, controls, components,
            componentCount = _document.Project.Views.SelectMany(v => v.Root.DescendantsAndSelf()).Count(),
            viewport = new { width = ActualWidth, height = ActualHeight }, tick = _ticks
        });
    }
    public void Dispose() { _timer.Stop(); _surface.Dispose(); _runtime?.Dispose(); }
    private sealed record ExplorerItem(string Name, string? View = null, string? ComponentId = null) { public override string ToString() => Name; }
}
