using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScadaSpace.Core;
using ScadaSpace.Designer;
using ScadaSpace.Expressions;

namespace ScadaSpace.Controls;

/// <summary>Reusable model inspector with validated edits and explicit tag/property/expression binding configuration.</summary>
public partial class PropertyInspector : Grid
{
    private readonly StackPanel _fields = new() { Spacing = 1 };
    private readonly TextBlock _heading = ControlTheme.Text("No component selected", 12);
    private readonly TextBlock _message = ControlTheme.Text("", 11, "#A45837");
    private readonly List<Action> _live = [];
    private DesignerDocument? _document;
    private ViewHost? _host;
    private string? _id;
    private long _inspectionVersion;
    private readonly Dictionary<string, FrameworkElement> _editorFields = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, FrameworkElement> EditorFields => _editorFields;
    public event Action<string>? Error;
    public PropertyInspector()
    {
        RowDefinitions.Add(new() { Height = new GridLength(33) }); RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new()); RowDefinitions.Add(new() { Height = GridLength.Auto });
        var title = new Border { Child = _heading, Padding = new Thickness(8, 0, 5, 0), Background = ControlTheme.Brush("#E7ECEF") }; Children.Add(title);
        var filter = BuildFilterBar(); Grid.SetRow(filter, 1); Children.Add(filter);
        var scroll = new ScrollViewer { Content = _fields, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 2); Children.Add(scroll); _message.Margin = new Thickness(6); _message.TextWrapping = TextWrapping.Wrap; Grid.SetRow(_message, 3); Children.Add(_message);
    }
    public void Inspect(DesignerDocument document, string? id, ViewHost host)
    {
        _inspectionVersion++; ResetFilterRows();
        _creationOpenCommands.Clear(); _bindingCommands.Clear(); _propertyLabels.Clear();
        _document = document; _host = host; _id = id; _fields.Children.Clear(); _editorFields.Clear(); _choices.Clear(); _colorEditors.Clear(); _switchEditors.Clear(); _live.Clear(); _message.Text = ""; EventConfigurationButton = null;
        var node = id is null ? null : document.Find(id);
        if (node is null) { _heading.Text = "No component selected"; _fields.Children.Add(new TextBlock { Text = "Select a component on the canvas or in the project browser.\n\nDrag the lower-right handle to resize. Use Ctrl-click for multiple selection.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10), Foreground = ControlTheme.Brush("#758792") }); FinishFilterRows(false); return; }
        _heading.Text = node.Name + "  ·  " + node.Type;
        Section("META");
        Field("name", node.Name, text => Edit("Rename component", n => n.Name = text));
        var locked = new CheckBox { Content = "Lock position", IsChecked = node.Locked, FontSize = 11, MinHeight = 29, Margin = new Thickness(6, 0, 0, 0) };
        locked.Checked += (_, _) => Edit("Lock component", n => n.Locked = true); locked.Unchecked += (_, _) => Edit("Unlock component", n => n.Locked = false); AddFilterRow(locked, "locked");
        Section("PROPS");
        foreach (var pair in node.Props.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var property = pair.Key; var value = pair.Value; var bound = node.Bindings.ContainsKey(property) || node.Bindings.ContainsKey("props." + property);
            if (TryBuildTypedSymbolProperty(node, property, value, bound) || TryBuildLedChoice(node, property, value, bound)) continue;
            var box = Field(property, PropertyValueText.Format(value), text => Edit("Set " + property, n => n.Props[property] = PropertyValueText.Parse(text, value.Kind)), async () => await EditBindingAsync(property), bound, value.Kind);
            if (bound) TrackBoundText(property, (text, kind) => { box.Text = text; box.Foreground = ValueBrush(kind); });
        }
        BuildAddPropertyCommand(false);
        Section("POSITION");
        Field("x", node.X.ToString(CultureInfo.InvariantCulture), text => Edit("Set X", n => n.X = Parse(text)), kind: ScalarKind.Number);
        Field("y", node.Y.ToString(CultureInfo.InvariantCulture), text => Edit("Set Y", n => n.Y = Parse(text)), kind: ScalarKind.Number);
        Field("width", node.Width.ToString(CultureInfo.InvariantCulture), text => Edit("Set width", n => n.Width = Parse(text)), kind: ScalarKind.Number);
        Field("height", node.Height.ToString(CultureInfo.InvariantCulture), text => Edit("Set height", n => n.Height = Parse(text)), kind: ScalarKind.Number);
        Field("rotation", node.Rotation.ToString(CultureInfo.InvariantCulture), text => Edit("Rotate component", n => n.Rotation = Parse(text)), kind: ScalarKind.Number);
        if (node.Type == "Container")
        {
            var layout = new ComboBox { ItemsSource = Enum.GetValues<LayoutKind>(), SelectedItem = node.Layout, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(6), MinHeight = 30 };
            layout.SelectionChanged += (_, _) => { if (layout.SelectedItem is LayoutKind selected) Edit("Change layout", n => n.Layout = selected); }; AddFilterRow(layout, "layout");
        }
        Section("STYLE");
        Field("classes", string.Join(", ", node.StyleClasses), text => Edit("Set style classes", n => n.StyleClasses = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToList()));
        foreach (var pair in node.Style.OrderBy(p => p.Key)) { var key = pair.Key; var value = pair.Value; Field(key, PropertyValueText.Format(value), text => Edit("Set style", n => n.Style[key] = PropertyValueText.Parse(text, value.Kind)), kind: value.Kind); }
        AddFilterRow(ControlTheme.Button("Edit style JSON…", async () => await EditJsonAsync("Style", JsonSerializer.Serialize(node.Style, ProjectCodec.Options), text => Edit("Edit style", n => n.Style = JsonSerializer.Deserialize<Dictionary<string, Scalar>>(text, ProjectCodec.Options) ?? throw new FormatException("Style cannot be null.")))), "Edit style JSON", property: false);
        Section("CUSTOM");
        foreach (var pair in node.Custom.OrderBy(p => p.Key)) { var key = pair.Key; var value = pair.Value; Field(key, PropertyValueText.Format(value), text => Edit("Set custom property", n => n.Custom[key] = PropertyValueText.Parse(text, value.Kind)), kind: value.Kind); }
        BuildAddPropertyCommand(true);
        Section("EVENTS");
        foreach (var pair in node.Events) AddFilterRow(ControlTheme.Text("  " + pair.Key + "  ·  " + pair.Value.Count + " actions", 11), pair.Key, property: false);
        BuildEventCommand(node);
        RefreshValues(); FinishFilterRows(true);
    }
    public void RefreshValues() { foreach (var update in _live) update(); }
    private static double Parse(string text)
    {
        var value = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (!double.IsFinite(value)) throw new FormatException("A finite value is required.");
        return value;
    }
    private TextBox Field(string name, string text, Action<string> commit, Action? binding = null, bool bound = false, ScalarKind kind = ScalarKind.String)
    {
        var row = BuildPropertyRow(name, binding, bound);
        var box = new TextBox { Text = text, FontSize = 11, MinHeight = 27, MinWidth = 0, Padding = new Thickness(4, 2, 4, 2), BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = ControlTheme.Brush("#E4E8EB"), Background = ControlTheme.Brush(bound ? "#EDF8FC" : "#FFFFFF"), IsReadOnly = bound, Foreground = ValueBrush(kind) };
        RegisterPropertyEditor(name, box);
        var original = text;
        var owner = _document; var componentId = _id; var generation = owner?.Generation; var inspection = _inspectionVersion;
        void Apply()
        {
            if (bound || box.Text == original || _inspectionVersion != inspection || !ReferenceEquals(_document, owner) || _id != componentId || owner?.Generation != generation) return;
            try { commit(box.Text); original = box.Text; _message.Text = ""; }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or JsonException or OverflowException) { _message.Text = ex.Message; box.Text = original; Error?.Invoke(ex.Message); }
        }
        box.LostFocus += (_, _) => Apply(); box.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { Apply(); e.Handled = true; } };
        Grid.SetColumn(box, 2); row.Children.Add(box);
        AddFilterRow(row, name, bound); return box;
    }
    private void Edit(string label, Action<ComponentNode> action)
    {
        if (_document is null || _id is null) return;
        _document.Execute(label, _ => action(_document.Find(_id) ?? throw new InvalidOperationException("Component no longer exists.")));
    }
    public async Task EditBindingAsync(string property)
    {
        var node = _id is null ? null : _document?.Find(_id); if (node is null) return;
        var bindingKey = node.Bindings.ContainsKey(property) ? property : node.Bindings.ContainsKey("props." + property) ? "props." + property : property;
        node.Bindings.TryGetValue(bindingKey, out var old);
        var kind = new ComboBox { Header = "Binding type", ItemsSource = Enum.GetValues<BindingKind>(), SelectedItem = old?.Kind ?? BindingKind.Tag, MinWidth = 420 };
        var source = new TextBox { Header = "Tag path / property path / expression", Text = old?.Source ?? "[default]Plant/Tank01/Level", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 78 };
        var enabled = new CheckBox { Content = "Enable binding", IsChecked = old?.Enabled ?? true };
        var bidirectional = new CheckBox { Content = "Bidirectional (direct tag without transforms only)", IsChecked = old?.Bidirectional ?? false };
        var transforms = new TextBox { Header = "Transform chain (JSON)", Text = JsonSerializer.Serialize(old?.Transforms ?? [], ProjectCodec.Options), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, MaxHeight = 180, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("monospace"), FontSize = 12 };
        var hint = new TextBlock { Text = "Tag: [default]Plant/Tank01/Level\nProperty: Tank01.props.value\nExpression: round({[default]Plant/Tank01/Level}, 1)\nTransforms: Expression, Scale, Format, Map. Use value in expression transforms.", FontSize = 11, TextWrapping = TextWrapping.Wrap };
        var error = new TextBlock { Foreground = ControlTheme.Brush("#B14E37"), TextWrapping = TextWrapping.Wrap };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = node.Name + ".props." + property, PrimaryButtonText = "Apply", SecondaryButtonText = old is null ? "" : "Remove binding", CloseButtonText = "Cancel", Content = new ScrollViewer { Content = new StackPanel { Spacing = 8, Children = { kind, source, enabled, bidirectional, transforms, hint, error } } } };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                var binding = new BindingDefinition { Kind = (BindingKind)kind.SelectedItem, Source = source.Text, Enabled = enabled.IsChecked == true, Bidirectional = bidirectional.IsChecked == true, Transforms = JsonSerializer.Deserialize<List<TransformDefinition>>(transforms.Text, ProjectCodec.Options) ?? throw new FormatException("Transform chain must be an array.") };
                if (binding.Kind == BindingKind.Expression) new ExpressionEngine().Compile(binding.Source);
                foreach (var transform in binding.Transforms.Where(t => t.Kind == TransformKind.Expression)) new ExpressionEngine().Compile(transform.Expression);
                Edit("Configure binding", n => n.Bindings[bindingKey] = binding);
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException or JsonException) { args.Cancel = true; error.Text = ex.Message; }
        };
        dialog.SecondaryButtonClick += (_, _) => Edit("Remove binding", n => n.Bindings.Remove(bindingKey));
        await dialog.ShowAsync();
    }
    public async Task EditJsonAsync(string title, string json, Action<string> commit, string? description = null)
    {
        var text = new TextBox { Text = json, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinWidth = 420, MinHeight = 300, MaxHeight = 520, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("monospace"), FontSize = 12 };
        var error = new TextBlock { Foreground = ControlTheme.Brush("#B14E37"), TextWrapping = TextWrapping.Wrap };
        var panel = new StackPanel { Spacing = 8 };
        if (description is not null) panel.Children.Add(new TextBlock { Text = description, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(text); panel.Children.Add(error);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, PrimaryButtonText = "Apply", CloseButtonText = "Cancel", Content = panel };
        dialog.PrimaryButtonClick += (_, e) => { try { if (text.Text.Length > ProjectCodec.MaximumBytes) throw new FormatException("Input exceeds the size limit."); commit(text.Text); } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or JsonException) { e.Cancel = true; error.Text = ex.Message; } };
        await dialog.ShowAsync();
    }
}
