using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ScadaSpace.Core;

namespace ScadaSpace.Controls;

/// <summary>Transactional page/dock editor. Changes are staged in a clone until the caller accepts CreateProject().</summary>
public partial class PageConfigurationEditor : Grid
{
    private readonly ProjectDefinition _draft;
    private readonly ComboBox _target = new() { Header = "Page configuration", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _path = new() { Header = "Page URL" };
    private readonly TextBox _title = new() { Header = "Page title" };
    private readonly ComboBox _view = new() { Header = "Primary view", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _corner = new() { Header = "Corner priority", ItemsSource = new[] { "Inherited", "TopBottom", "LeftRight" }, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _parameters = new() { Header = "Primary view parameters (JSON object)", AcceptsReturn = true, MinHeight = 60 };
    private readonly StackPanel _dockFields = new() { Spacing = 6 };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Foreground = ControlTheme.Brush("#A73828") };
    private readonly List<Func<DockDefinition?>> _readDocks = [];
    private readonly Dictionary<string, FrameworkElement> _fields = new(StringComparer.Ordinal);
    private int _page = -1;
    private bool _switching;
    private bool _loaded;
    public IReadOnlyDictionary<string, FrameworkElement> EditorFields => _fields;
    public PageConfigurationEditor(ProjectDefinition project)
    {
        _draft = ProjectCodec.Clone(project);
        var content = new StackPanel { Spacing = 8, MinWidth = 460, MaxWidth = 700 };
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var add = ControlTheme.Button("Add page", () => TryEdit(AddPage));
        var delete = ControlTheme.Button("Delete page", () => TryEdit(DeletePage));
        Track("add", add); Track("delete", delete); Track("target", _target); Track("path", _path); Track("title", _title); Track("view", _view); Track("corner", _corner); Track("parameters", _parameters);
        commands.Children.Add(add); commands.Children.Add(delete);
        content.Children.Add(_target); content.Children.Add(commands);
        content.Children.Add(new TextBlock { Text = "Shared docks are inherited by every page. A page-local dock replaces the shared dock on the same edge. Choose (inherit / none) to remove an override.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        content.Children.Add(_path); content.Children.Add(_title); content.Children.Add(_view); content.Children.Add(_parameters); content.Children.Add(_corner); content.Children.Add(_dockFields); content.Children.Add(_error);
        Children.Add(new ScrollViewer { Content = content, MaxHeight = 540, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        _view.ItemsSource = _draft.Views.Select(v => v.Path).ToArray();
        _target.SelectionChanged += (_, _) =>
        {
            if (_switching) return;
            var next = _target.SelectedIndex - 1;
            if (!TryEdit(() => { if (_loaded) SaveCurrent(); _page = next; LoadCurrent(); }))
            { _switching = true; _target.SelectedIndex = _page + 1; _switching = false; }
        };
        ReloadTargets(0);
    }
    private void Track(string id, FrameworkElement control)
    { _fields[id] = control; AutomationProperties.SetAutomationId(control, "page-editor-" + id); }
    private bool TryEdit(Action action)
    {
        try { action(); _error.Text = ""; return true; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or System.Text.Json.JsonException) { _error.Text = ex.Message; return false; }
    }
    private void ReloadTargets(int selected)
    {
        _switching = true;
        _target.ItemsSource = new[] { "Shared settings" }.Concat(_draft.Pages.Select(p => p.Path)).ToArray();
        _target.SelectedIndex = selected; _page = selected - 1; _switching = false; LoadCurrent();
    }
    private void LoadCurrent()
    {
        var page = _page >= 0 ? _draft.Pages[_page] : null;
        _path.Text = page?.Path ?? "Shared settings"; _title.Text = page?.Title ?? ""; _view.SelectedItem = page?.View ?? _draft.Views[0].Path;
        _path.IsEnabled = _title.IsEnabled = _view.IsEnabled = _parameters.IsEnabled = page is not null;
        _parameters.Text = Scalar.Object(page?.Parameters ?? []).ToJson();
        _corner.SelectedIndex = page is null ? (int)_draft.SharedCornerPriority + 1 : page.CornerPriority.HasValue ? (int)page.CornerPriority.Value + 1 : 0;
        _dockFields.Children.Clear(); _readDocks.Clear();
        foreach (var key in _fields.Keys.Where(k => k.StartsWith("dock-", StringComparison.Ordinal)).ToArray()) _fields.Remove(key);
        var docks = page?.Docks ?? _draft.SharedDocks;
        foreach (var edge in Enum.GetValues<DockEdge>()) AddDockFields(edge, docks.FirstOrDefault(d => d.Edge == edge));
        _loaded = true;
    }
    private void AddDockFields(DockEdge edge, DockDefinition? old)
    {
        var panel = new StackPanel { Spacing = 6 };
        var view = new ComboBox { Header = "View", ItemsSource = new[] { "(inherit / none)" }.Concat(_draft.Views.Select(v => v.Path)).ToArray(), SelectedItem = old?.View ?? "(inherit / none)", HorizontalAlignment = HorizontalAlignment.Stretch };
        var id = new TextBox { Header = "Dock ID", Text = old?.Id ?? edge.ToString().ToLowerInvariant() };
        var display = new ComboBox { Header = "Display", ItemsSource = Enum.GetValues<DockDisplay>(), SelectedItem = old?.Display ?? DockDisplay.OnDemand, HorizontalAlignment = HorizontalAlignment.Stretch };
        var mode = new ComboBox { Header = "Content", ItemsSource = Enum.GetValues<DockContent>(), SelectedItem = old?.Content ?? DockContent.Push, HorizontalAlignment = HorizontalAlignment.Stretch };
        NumberBox Number(string header, double value) => new() { Header = header, Value = value, Minimum = 0, Maximum = 100000, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var size = Number("Size (pixels)", old?.Size ?? 240); var min = Number("Minimum size", old?.MinimumSize ?? 48); var max = Number("Maximum size", old?.MaximumSize ?? 1200); var breakpoint = Number("Auto breakpoint (page width)", old?.AutoBreakpoint ?? 800);
        var resize = new CheckBox { Content = "Resizable", IsChecked = old?.Resizable ?? true };
        var modal = new CheckBox { Content = "Modal (on-demand only)", IsChecked = old?.Modal ?? false, IsEnabled = old is null || old.Display == DockDisplay.OnDemand };
        var handle = new CheckBox { Content = "Show open/close handle", IsChecked = old?.ShowHandle ?? true };
        var parameters = new TextBox { Header = "View parameters (JSON object)", Text = Scalar.Object(old?.Parameters ?? []).ToJson(), AcceptsReturn = true, MinHeight = 60 };
        display.SelectionChanged += (_, _) => { modal.IsEnabled = (DockDisplay)display.SelectedItem == DockDisplay.OnDemand; if (!modal.IsEnabled) modal.IsChecked = false; };
        foreach (var control in new FrameworkElement[] { view, id, display, mode, size, min, max, breakpoint, resize, modal, handle, parameters }) panel.Children.Add(control);
        var expander = new Expander { Header = edge + (old is null ? " · inherited / none" : " · " + old.View), Content = panel, HorizontalAlignment = HorizontalAlignment.Stretch };
        Track("dock-" + edge, expander); Track("dock-" + edge + "-view", view); Track("dock-" + edge + "-size", size);
        _dockFields.Children.Add(expander);
        _readDocks.Add(() => view.SelectedIndex <= 0 ? null : new DockDefinition
        {
            Id = id.Text.Trim(), View = (string)view.SelectedItem, Edge = edge, Display = (DockDisplay)display.SelectedItem, Content = (DockContent)mode.SelectedItem,
            Size = size.Value, MinimumSize = min.Value, MaximumSize = max.Value, AutoBreakpoint = breakpoint.Value,
            Resizable = resize.IsChecked == true, Modal = modal.IsChecked == true, ShowHandle = handle.IsChecked == true, Parameters = ParseParameters(parameters.Text)
        });
    }
    private static Dictionary<string, Scalar> ParseParameters(string json)
    {
        var value = Scalar.FromJson(json);
        if (value.Kind != ScalarKind.Object) throw new FormatException("Parameters must be a JSON object.");
        return value.Fields.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
    }
    private void SaveCurrent()
    {
        var docks = _readDocks.Select(read => read()).Where(d => d is not null).Cast<DockDefinition>().ToList();
        var corner = _corner.SelectedIndex == 0 ? (CornerPriority?)null : (CornerPriority)(_corner.SelectedIndex - 1);
        if (_page < 0) { _draft.SharedDocks = docks; _draft.SharedCornerPriority = corner ?? CornerPriority.TopBottom; }
        else
        {
            var page = _draft.Pages[_page];
            var path = _path.Text.Trim(); _ = new PageRoutePattern(path);
            var parameters = ParseParameters(_parameters.Text);
            page.Path = path; page.Title = _title.Text; page.View = (string)_view.SelectedItem; page.Parameters = parameters; page.Docks = docks; page.CornerPriority = corner;
        }
    }
    private void AddPage()
    {
        SaveCurrent(); if (_draft.Pages.Count >= 512) throw new InvalidOperationException("Page limit reached.");
        var i = 1; while (_draft.Pages.Any(p => p.Path == "/page" + i)) i++;
        _draft.Pages.Add(new() { Path = "/page" + i, View = _draft.Views[0].Path, Title = "New page" }); ReloadTargets(_draft.Pages.Count);
    }
    private void DeletePage()
    {
        if (_page < 0) throw new InvalidOperationException("Shared settings cannot be deleted.");
        _draft.Pages.RemoveAt(_page); ReloadTargets(0);
    }
    public ProjectDefinition CreateProject()
    {
        SaveCurrent(); var errors = ProjectCodec.Validate(_draft);
        if (errors.Count > 0) throw new FormatException(string.Join("\n", errors.Select(e => e.Path + ": " + e.Message)));
        return ProjectCodec.Clone(_draft);
    }
}
