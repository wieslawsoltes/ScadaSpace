using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ScadaSpace.Core;
using ScadaSpace.Graphics;
using ScadaSpace.Layout;
using ScadaSpace.Runtime;

namespace ScadaSpace.Controls;

/// <summary>Reusable Perspective-style view host. The project model is never mutated by runtime input.</summary>
public partial class ViewHost : Canvas, IDisposable
{
    private readonly List<Action> _refresh = [];
    private readonly List<ViewHost> _embedded = [];
    private readonly Dictionary<string, FrameworkElement> _elements = new(StringComparer.Ordinal);
    private bool _updating;
    private int _depth;
    private string _ancestry = "";
    public ProjectRuntime? Runtime { get; private set; }
    public RuntimeSession? Session { get; private set; }
    public ViewInstance? Instance { get; private set; }
    public bool IsEditing { get; private set; }
    public IReadOnlyDictionary<string, FrameworkElement> Elements => _elements;
    public event Action<string>? Error;
    public event Action? ValueChanged;
    public ViewHost() { Background = ControlTheme.Brush("#F4F7F8"); }

    public void Load(ProjectRuntime runtime, string path, IReadOnlyDictionary<string, Scalar>? parameters = null, bool editing = false, RuntimeSession? session = null, int depth = 0, string ancestry = "", Scalar? pageProperties = null)
    {
        Dispose(); Children.Clear(); _elements.Clear();
        if (depth > 12 || ancestry.Split('|').Contains(path, StringComparer.Ordinal)) throw new InvalidOperationException("Recursive embedded view: " + path);
        Runtime = runtime; Session = session ?? new(runtime); Instance = runtime.CreateView(path, parameters); IsEditing = editing;
        if (pageProperties is { } context) Instance.SetPageProperties(context);
        _depth = depth; _ancestry = ancestry + "|" + path;
        Width = Instance.View.Width; Height = Instance.View.Height;
        var root = BuildNode(Instance.View.Root); root.Width = Width; root.Height = Height;
        Children.Add(root); Refresh();
    }
    public void Refresh()
    {
        if (Instance is null || _updating) return;
        _updating = true;
        try
        {
            Instance.Evaluate();
            foreach (var update in _refresh)
            {
                try { update(); }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or JsonException) { Error?.Invoke(ex.Message); }
            }
            foreach (var host in _embedded)
            {
                host.Instance?.SetPageProperties(Instance.PageProperties);
                host.Refresh();
            }
        }
        finally { _updating = false; }
    }
    private FrameworkElement BuildNode(ComponentNode node)
    {
        var style = Runtime!.Styles.Resolve(node).Values;
        FrameworkElement element;
        if (node.Type == "Container")
        {
            var panel = new ModelPanel(node);
            foreach (var child in node.Children) panel.Add(child, BuildNode(child));
            element = panel;
        }
        else if (Enum.TryParse<SymbolKind>(node.Type, out var kind))
        {
            element = BuildSymbol(node, kind);
        }
        else switch (node.Type)
        {
            case "Label":
                var label = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
                string? previousForeground = null;
                _refresh.Add(() =>
                {
                    var value = Read(node, "text"); label.Text = value.IsGood ? value.Value.ToString() : "⚠ " + value.Quality;
                    if (!style.ContainsKey("fontSize")) label.FontSize = Math.Clamp(Number(node, "fontSize", 14), 6, 200);
                    if (!style.ContainsKey("foreground"))
                    {
                        var foreground = Text(node, "foreground", "#354B58");
                        if (foreground != previousForeground) { label.Foreground = ControlTheme.Brush(foreground); previousForeground = foreground; }
                    }
                });
                element = label; break;
            case "Progress":
                var progress = new ProgressBar { VerticalAlignment = VerticalAlignment.Center, MinHeight = 12 };
                _refresh.Add(() => { progress.Minimum = Number(node, "minimum"); progress.Maximum = Math.Max(progress.Minimum + 1, Number(node, "maximum", 100)); progress.Value = Number(node, "value"); });
                element = progress; break;
            case "Button":
                var button = ControlTheme.Button(Text(node, "text", "Button"), () => { Action(node, "onAction"); Action(node, "onActionPerformed"); });
                _refresh.Add(() => { button.Content = Text(node, "text", "Button"); button.IsEnabled = Flag(node, "enabled", true); }); element = button; break;
            case "ToggleSwitch":
                var toggle = new ToggleSwitch { MinWidth = 0, MinHeight = 0, FontSize = 12, OffContent = "", OnContent = "" };
                toggle.Toggled += (_, _) => Write(node, "value", Scalar.From(toggle.IsOn));
                _refresh.Add(() => { toggle.IsOn = Flag(node, "value"); toggle.Header = Text(node, "text"); toggle.IsEnabled = Writable(node, "value"); }); element = toggle; break;
            case "CheckBox":
                var check = new CheckBox { FontSize = 12, MinHeight = 0 };
                check.Checked += (_, _) => Write(node, "value", Scalar.From(true)); check.Unchecked += (_, _) => Write(node, "value", Scalar.From(false));
                _refresh.Add(() => { check.IsChecked = Flag(node, "value"); check.Content = Text(node, "text"); check.IsEnabled = Writable(node, "value"); }); element = check; break;
            case "NumericEntry":
                var number = new HmiNumericEntry { FontSize = 13, MinWidth = 0, MinHeight = 0 };
                number.ValueChanged += (_, e) => { if (double.IsFinite(e.NewValue)) Write(node, "value", Scalar.From(e.NewValue)); };
                _refresh.Add(() => { number.Minimum = Number(node, "minimum"); number.Maximum = Math.Max(number.Minimum, Number(node, "maximum", 100)); number.SmallChange = Math.Max(.001, Number(node, "step", 1)); if (!number.HasEditFocus) number.Value = Number(node, "value"); number.IsEnabled = Writable(node, "value"); }); element = number; break;
            case "TextField":
                var text = new TextBox { FontSize = 13, MinWidth = 0, MinHeight = 0, Padding = new Thickness(6, 3, 6, 3) };
                text.LostFocus += (_, _) => Write(node, "text", Scalar.From(text.Text));
                text.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) Write(node, "text", Scalar.From(text.Text)); };
                _refresh.Add(() => { if (text.FocusState == FocusState.Unfocused) text.Text = Text(node, "text"); text.PlaceholderText = Text(node, "placeholder"); text.IsReadOnly = !Writable(node, "text"); }); element = text; break;
            case "Slider":
                var slider = new Slider { MinWidth = 0, MinHeight = 0, VerticalAlignment = VerticalAlignment.Center };
                slider.ValueChanged += (_, e) => Write(node, "value", Scalar.From(e.NewValue));
                _refresh.Add(() => { slider.Minimum = Number(node, "minimum"); slider.Maximum = Math.Max(slider.Minimum + .001, Number(node, "maximum", 100)); slider.StepFrequency = Math.Max(.001, Number(node, "step", 1)); slider.Value = Number(node, "value"); slider.IsEnabled = Writable(node, "value"); }); element = slider; break;
            case "Dropdown":
                var dropdown = new ComboBox { FontSize = 13, MinWidth = 0, MinHeight = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
                var optionText = "";
                dropdown.SelectionChanged += (_, _) => { if (dropdown.SelectedItem is string selected) Write(node, "value", Scalar.From(selected)); };
                _refresh.Add(() => { var options = Text(node, "options", "Off,Hand,Auto"); if (optionText != options) { optionText = options; dropdown.ItemsSource = options.Split(',').Select(s => s.Trim()).ToArray(); } dropdown.SelectedItem = Text(node, "value"); dropdown.IsEnabled = Writable(node, "value"); }); element = dropdown; break;
            case "RadioGroup":
                var radios = new StackPanel(); var groupName = Guid.NewGuid().ToString("N");
                foreach (var option in node.Text("options", "Off,Hand,Auto").Split(',').Take(64))
                {
                    var radio = new RadioButton { Content = option.Trim(), GroupName = groupName, FontSize = 12, MinHeight = 28 };
                    radio.Checked += (_, _) => Write(node, "value", Scalar.From(option.Trim()));
                    _refresh.Add(() => { radio.IsChecked = Text(node, "value") == option.Trim(); radio.IsEnabled = Writable(node, "value"); }); radios.Children.Add(radio);
                }
                element = radios; break;
            case "DateTimeInput":
                var date = new CalendarDatePicker { FontSize = 12, MinWidth = 0, MinHeight = 0 };
                date.DateChanged += (_, e) => { if (e.NewDate is { } selected) Write(node, "value", Scalar.From(selected.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))); };
                _refresh.Add(() => { if (DateTimeOffset.TryParse(Text(node, "value"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)) date.Date = parsed; date.IsEnabled = Writable(node, "value"); }); element = date; break;
            case "TrendChart": case "BarChart": case "PieChart":
                var chart = new HmiChart { Kind = node.Type == "TrendChart" ? ChartKind.Trend : node.Type == "BarChart" ? ChartKind.Bar : ChartKind.Pie };
                var firstRefresh = true;
                _refresh.Add(() =>
                {
                    var paths = Text(node, "series").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Take(16).ToArray();
                    var series = paths.Select((path, i) => new PlotSeries(path, ChartRenderer.Palette[i % ChartRenderer.Palette.Length], Runtime!.History.Query(path))).ToArray();
                    chart.SetData(series, new(Text(node, "title", "Live trends"), Number(node, "minimum"), Number(node, "maximum", 100), firstRefresh ? Number(node, "windowSeconds", 120) : chart.Options.WindowSeconds)); firstRefresh = false;
                }); element = chart; break;
            case "Tree": case "TagBrowseTree":
                element = BuildTree(node); break;
            case "Table": case "AlarmStatusTable": case "AlarmJournalTable":
                var table = new HmiDataTable();
                if (node.Type == "AlarmStatusTable")
                {
                    table.AddCommand("Acknowledge", key => { if (!IsEditing && Guid.TryParse(key, out var id)) { Runtime!.Alarms.Acknowledge(id, Session!.Actor); Refresh(); ValueChanged?.Invoke(); } });
                    table.AddCommand("Shelve 5m", key => { if (!IsEditing && Guid.TryParse(key, out var id)) { Runtime!.Alarms.Shelve(id, TimeSpan.FromMinutes(5), Session!.Actor); Refresh(); ValueChanged?.Invoke(); } });
                }
                ConnectTable(node, table); element = table; break;
            case "EmbeddedView": case "FlexRepeater":
                element = BuildEmbedded(node); break;
            default: element = ControlTheme.Text("Unsupported component: " + node.Type, 12, "#B94A37"); break;
        }
        element.MinWidth = 0; element.MinHeight = 0;
        element.Tag = node.Id;
        AutomationProperties.SetAutomationId(element, "component-" + node.Name);
        AutomationProperties.SetName(element, node.Name);
        element.IsHitTestVisible = !IsEditing;
        if (node.Rotation != 0) { element.RenderTransform = new RotateTransform { Angle = node.Rotation }; element.RenderTransformOrigin = new Point(.5, .5); }
        // Native events are raised only in runtime mode. Existing onAction remains a legacy button event.
        if (node.Events.ContainsKey("onClick")) element.Tapped += (_, _) => Action(node, "onClick");
        if (node.Events.ContainsKey("onDoubleClick")) element.DoubleTapped += (_, _) => Action(node, "onDoubleClick");
        if (node.Events.ContainsKey("onContextMenu")) element.RightTapped += (_, _) => Action(node, "onContextMenu");
        _elements[node.Id] = element;
        return ComponentStyle.Apply(element, style);
    }
    public new void Dispose()
    { foreach (var symbol in _elements.Values.OfType<HmiSymbol>()) symbol.Dispose(); foreach (var host in _embedded) host.Dispose(); _embedded.Clear(); _refresh.Clear(); Instance = null; }
    private partial class ModelPanel(ComponentNode node) : Canvas
    {
        private readonly Dictionary<string, FrameworkElement> _children = new(StringComparer.Ordinal);
        public void Add(ComponentNode child, FrameworkElement element)
        {
            _children[child.Id] = element; Children.Add(element);
            if (_children.Count == 1) SizeChanged += (_, _) => ArrangeChildren();
        }
        private void ArrangeChildren()
        {
            foreach (var slot in LayoutEngine.Arrange(node, ActualWidth, ActualHeight))
            {
                var child = _children[slot.Node.Id]; child.Visibility = slot.Visible ? Visibility.Visible : Visibility.Collapsed;
                Canvas.SetLeft(child, slot.Bounds.X); Canvas.SetTop(child, slot.Bounds.Y); child.Width = slot.Bounds.Width; child.Height = slot.Bounds.Height;
            }
        }
    }
}
