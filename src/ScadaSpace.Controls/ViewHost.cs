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
    private readonly Dictionary<string, Scalar> _localValues = new(StringComparer.Ordinal);
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

    public void Load(ProjectRuntime runtime, string path, IReadOnlyDictionary<string, Scalar>? parameters = null, bool editing = false, RuntimeSession? session = null, int depth = 0, string ancestry = "")
    {
        Dispose(); Children.Clear(); _elements.Clear(); _localValues.Clear();
        if (depth > 12 || ancestry.Split('|').Contains(path, StringComparer.Ordinal)) throw new InvalidOperationException("Recursive embedded view: " + path);
        Runtime = runtime; Session = session ?? new(runtime); Instance = runtime.CreateView(path, parameters); IsEditing = editing;
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
            foreach (var host in _embedded) host.Refresh();
        }
        finally { _updating = false; }
    }
    public QualifiedValue Read(ComponentNode node, string property)
    {
        if (Instance is null) return QualifiedValue.Bad(Quality.BadDisabled, DateTimeOffset.UtcNow);
        if (!node.Bindings.ContainsKey(property) && _localValues.TryGetValue(node.Id + "." + property, out var value)) return QualifiedValue.Good(value, DateTimeOffset.UtcNow);
        return Instance.Read(node, property);
    }
    private Scalar Value(ComponentNode node, string property, Scalar fallback = default)
    {
        if (!node.Props.ContainsKey(property) && !node.Bindings.ContainsKey(property)) return fallback;
        return Read(node, property).Value;
    }
    private double Number(ComponentNode node, string property, double fallback = 0)
    { try { return Value(node, property, Scalar.From(fallback)).AsNumber(); } catch (InvalidOperationException) { return fallback; } }
    private string Text(ComponentNode node, string property, string fallback = "") => Value(node, property, Scalar.From(fallback)).ToString();
    private bool Flag(ComponentNode node, string property, bool fallback = false) => Value(node, property, Scalar.From(fallback)).AsBoolean();
    private void Write(ComponentNode node, string property, Scalar value)
    {
        if (_updating || IsEditing || Instance is null || Session is null) return;
        if (node.Bindings.ContainsKey(property))
        {
            var old = Read(node, property);
            var result = Instance.Write(node, property, value, Session.Actor, old.Revision);
            if (!result.Success) Error?.Invoke(result.Message);
        }
        else _localValues[node.Id + "." + property] = value;
        ValueChanged?.Invoke(); Refresh();
    }
    private void Action(ComponentNode node, string eventName)
    {
        if (IsEditing) return;
        try { Session?.Dispatch(node, eventName); ValueChanged?.Invoke(); }
        catch (InvalidOperationException ex) { Error?.Invoke(ex.Message); }
    }
    private bool Writable(ComponentNode node, string property)
    {
        if (IsEditing || !Flag(node, "enabled", true)) return false;
        if (!node.Bindings.TryGetValue(property, out var binding)) return true;
        return binding.Enabled && binding.Kind == BindingKind.Tag && binding.Bidirectional && binding.Transforms.Count == 0 && Read(node, property).IsGood;
    }
    private FrameworkElement BuildNode(ComponentNode node)
    {
        FrameworkElement element;
        if (node.Type == "Container")
        {
            var panel = new ModelPanel(node);
            foreach (var child in node.Children) panel.Add(child, BuildNode(child));
            element = panel;
        }
        else if (Enum.TryParse<SymbolKind>(node.Type, out var kind))
        {
            var symbol = new HmiSymbol { Kind = kind };
            _refresh.Add(() =>
            {
                var value = node.Props.ContainsKey("value") || node.Bindings.ContainsKey("value") ? Read(node, "value") : QualifiedValue.Good(Scalar.From(0), DateTimeOffset.UtcNow);
                symbol.Value = Number(node, "value"); symbol.Quality = value.Quality;
                symbol.Minimum = Number(node, "minimum"); symbol.Maximum = Number(node, "maximum", 100); symbol.Unit = Text(node, "unit");
                symbol.SymbolColor = Text(node, "color", "#48AFC0");
                symbol.Data = new(Format: Text(node, "format", "0.0"), Route: Text(node, "route", "elbow"), StrokeWidth: Number(node, "strokeWidth", 1), Stroke: Text(node, "stroke", "#9DAAB2"), Radius: Number(node, "radius"), Warning: Number(node, "warning", 85));
                symbol.Invalidate();
            });
            element = symbol;
        }
        else switch (node.Type)
        {
            case "Label":
                var label = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
                _refresh.Add(() => { var value = Read(node, "text"); label.Text = value.IsGood ? value.Value.ToString() : "⚠ " + value.Quality; label.FontSize = Math.Clamp(Number(node, "fontSize", 14), 6, 200); label.Foreground = ControlTheme.Brush(Text(node, "foreground", "#354B58")); });
                element = label; break;
            case "Progress":
                var progress = new ProgressBar { VerticalAlignment = VerticalAlignment.Center, MinHeight = 12 };
                _refresh.Add(() => { progress.Minimum = Number(node, "minimum"); progress.Maximum = Math.Max(progress.Minimum + 1, Number(node, "maximum", 100)); progress.Value = Number(node, "value"); });
                element = progress; break;
            case "Button":
                var button = ControlTheme.Button(Text(node, "text", "Button"), () => Action(node, "onAction"));
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
                var number = new NumberBox { FontSize = 13, MinWidth = 0, MinHeight = 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
                number.ValueChanged += (_, e) => { if (double.IsFinite(e.NewValue)) Write(node, "value", Scalar.From(e.NewValue)); };
                _refresh.Add(() => { number.Minimum = Number(node, "minimum"); number.Maximum = Math.Max(number.Minimum, Number(node, "maximum", 100)); number.SmallChange = Math.Max(.001, Number(node, "step", 1)); if (!number.FocusState.Equals(FocusState.Unfocused) && !_updating) return; number.Value = Number(node, "value"); number.IsEnabled = Writable(node, "value"); }); element = number; break;
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
            case "Table": case "AlarmStatusTable": case "AlarmJournalTable":
                var table = new HmiDataTable();
                if (node.Type == "AlarmStatusTable")
                {
                    table.AddCommand("Acknowledge", key => { if (!IsEditing && Guid.TryParse(key, out var id)) { Runtime!.Alarms.Acknowledge(id, Session!.Actor); Refresh(); ValueChanged?.Invoke(); } });
                    table.AddCommand("Shelve 5m", key => { if (!IsEditing && Guid.TryParse(key, out var id)) { Runtime!.Alarms.Shelve(id, TimeSpan.FromMinutes(5), Session!.Actor); Refresh(); ValueChanged?.Invoke(); } });
                }
                _refresh.Add(() => RefreshTable(node, table)); element = table; break;
            case "EmbeddedView": case "FlexRepeater":
                var panel = new Grid();
                var path = node.Text("view"); var count = node.Type == "FlexRepeater" ? Math.Clamp((int)node.Number("count", 1), 0, 100) : 1;
                for (var i = 0; i < count; i++)
                {
                    panel.ColumnDefinitions.Add(new());
                    var parameters = node.Props.Where(p => p.Key.StartsWith("param.", StringComparison.Ordinal)).ToDictionary(p => p.Key[6..], p => p.Value, StringComparer.Ordinal);
                    if (node.Type == "FlexRepeater") parameters["index"] = Scalar.From(i);
                    try
                    {
                        var child = new ViewHost(); child.Error += message => Error?.Invoke(message); child.Load(Runtime!, path, parameters, IsEditing, Session, _depth + 1, _ancestry);
                        var box = new Viewbox { Child = child, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(node.Number("gap", 4) / 2) };
                        Grid.SetColumn(box, i); panel.Children.Add(box); _embedded.Add(child);
                    }
                    catch (InvalidOperationException ex) { panel.Children.Add(ControlTheme.Text(ex.Message)); }
                }
                element = panel; break;
            default: element = ControlTheme.Text("Unsupported component: " + node.Type, 12, "#B94A37"); break;
        }
        element.MinWidth = 0; element.MinHeight = 0;
        element.Tag = node.Id;
        AutomationProperties.SetAutomationId(element, "component-" + node.Name);
        AutomationProperties.SetName(element, node.Name);
        element.IsHitTestVisible = !IsEditing;
        if (node.Rotation != 0) { element.RenderTransform = new RotateTransform { Angle = node.Rotation }; element.RenderTransformOrigin = new Point(.5, .5); }
        _elements[node.Id] = element;
        var style = new Dictionary<string, Scalar>(StringComparer.Ordinal);
        foreach (var className in node.StyleClasses) if (Runtime!.Project.Styles.TryGetValue(className, out var values)) foreach (var pair in values) style[pair.Key] = pair.Value;
        foreach (var pair in node.Style) style[pair.Key] = pair.Value;
        if (style.TryGetValue("opacity", out var opacity)) element.Opacity = Math.Clamp(opacity.AsNumber(), 0, 1);
        if (style.TryGetValue("background", out var background))
        {
            var border = new Border { Child = element, Background = ControlTheme.Brush(background.ToString()), BorderThickness = new Thickness(style.GetValueOrDefault("borderWidth", Scalar.From(1)).AsNumber()), BorderBrush = ControlTheme.Brush(style.GetValueOrDefault("borderColor", Scalar.From("#D5E0E5")).ToString()), CornerRadius = new CornerRadius(style.GetValueOrDefault("cornerRadius").Kind == ScalarKind.Number ? style["cornerRadius"].Number : 0), IsHitTestVisible = !IsEditing };
            return border;
        }
        return element;
    }
    private void RefreshTable(ComponentNode node, HmiDataTable table)
    {
        if (Runtime is null) return;
        if (node.Type == "AlarmStatusTable")
        {
            table.SetData([new("priority", "Priority", .7), new("name", "Alarm", 2), new("state", "State", 1.5), new("time", "Active (UTC)", 1)], Runtime.Alarms.Status(Flag(node, "showShelved", true)).Select(e => new TableRow(e.EventId.ToString(), [e.Priority.ToString(), e.Name, e.State, e.ActiveTime.UtcDateTime.ToString("HH:mm:ss")], e.Active ? "#D1903E" : "#78A290")).ToArray());
        }
        else if (node.Type == "AlarmJournalTable")
            table.SetData([new("time", "Timestamp (UTC)"), new("name", "Alarm", 2), new("transition", "Transition"), new("actor", "Actor")], Runtime.Alarms.Journal().Reverse().Select(e => new TableRow(e.Sequence.ToString(), [e.Timestamp.UtcDateTime.ToString("HH:mm:ss"), e.Name, e.Transition, e.Actor])).ToArray());
        else if (Text(node, "source", "tags") == "tags")
            table.SetData([new("path", "Tag path", 2.5), new("value", "Value"), new("unit", "Unit", .6), new("quality", "Quality")], Runtime.Tags.Browse(Text(node, "filter")).Select(d => { var value = Runtime.Tags.Read(d.Path); return new TableRow(d.Path, [d.Path, value.Value.ToString(), d.Unit, value.Quality.ToString()]); }).ToArray());
        else
        {
            var data = Text(node, "data", "[]");
            if (data.Length > 1024 * 1024) throw new FormatException("Table data exceeds 1 MiB.");
            using var json = JsonDocument.Parse(data);
            if (json.RootElement.ValueKind != JsonValueKind.Array) throw new FormatException("Table data must be an array of objects.");
            var objects = json.RootElement.EnumerateArray().Take(10000).ToArray();
            var keys = objects.Where(x => x.ValueKind == JsonValueKind.Object).SelectMany(x => x.EnumerateObject().Select(p => p.Name)).Distinct(StringComparer.Ordinal).Take(32).ToArray();
            table.SetData(keys.Select(k => new TableColumn(k, k)).ToArray(), objects.Select((o, i) => new TableRow(i.ToString(), keys.Select(k => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(k, out var v) ? v.ToString() : "").ToArray())).ToArray());
        }
    }
    public void Dispose()
    { foreach (var host in _embedded) host.Dispose(); _embedded.Clear(); _refresh.Clear(); }
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
