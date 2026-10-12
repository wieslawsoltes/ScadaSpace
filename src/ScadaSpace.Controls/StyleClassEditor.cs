using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ScadaSpace.Core;
using ScadaSpace.Designer;
using Windows.UI.Text;

namespace ScadaSpace.Controls;

/// <summary>Native class list, grouped property editor and preview; edits are detached until Apply.</summary>
public sealed partial class StyleClassEditor : Grid
{
    private readonly StyleClassDraft _draft;
    private readonly string? _component;
    private readonly ListView _classes = new() { FontSize = 12, SelectionMode = ListViewSelectionMode.Single };
    private readonly TextBox _filter = new() { PlaceholderText = "Filter style classes", MaxLength = 256, FontSize = 12 };
    private readonly TextBox _name = new() { Header = "Class name / folder path", MaxLength = 256, FontSize = 12, Text = "NewStyle" };
    private readonly Dictionary<string, StyleValueEditor> _properties = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FrameworkElement> _fields = new(StringComparer.Ordinal);
    private readonly CheckBox _renameReferences = new() { Content = "Update references on rename", IsChecked = true, FontSize = 11 };
    private readonly TextBlock _applied = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private IReadOnlyDictionary<string, Scalar>? _previewValues;
    private readonly CheckBox _assigned = new() { Content = "Assign to selected component", FontSize = 12 };
    private readonly TextBlock _usage = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _unknown = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _error = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = ControlTheme.Brush("#A73828") };
    private readonly TextBlock _sample = new() { Text = "Process heading\nTank 01 · 72.5 %", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _preview = new() { MinHeight = 160, Margin = new Thickness(0, 8, 0, 12) };
    private readonly StackPanel _form = new() { Spacing = 4 };
    private bool _sync, _assignmentTouched;
    private string? _selected;
    public IReadOnlyDictionary<string, FrameworkElement> EditorFields => _fields;
    public StyleClassDraft Draft => _draft;
    public string? SelectedClass => _selected;
    public string ErrorText => _error.Text;
    public FrameworkElement Preview => _preview;
    public IReadOnlyDictionary<string, StyleValueEditor> ValueEditors => _properties;
    public TextBlock PreviewText => _sample;

    public StyleClassEditor(StyleClassDraft draft, string? component = null, string? initialClass = null)
    {
        _draft = draft ?? throw new ArgumentNullException(nameof(draft)); _component = component;
        Background = ControlTheme.Brush("#F3F4F5"); ColumnSpacing = 14;
        ColumnDefinitions.Add(new() { Width = new GridLength(205) }); ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); ColumnDefinitions.Add(new() { Width = new GridLength(255) });
        RowDefinitions.Add(new()); RowDefinitions.Add(new() { Height = GridLength.Auto });
        var inventory = new Grid { Padding = new Thickness(8), Background = ControlTheme.Brush("#DFE3E6") };
        inventory.RowDefinitions.Add(new() { Height = new GridLength(28) }); inventory.RowDefinitions.Add(new() { Height = new GridLength(36) }); inventory.RowDefinitions.Add(new());
        inventory.Children.Add(ControlTheme.Text("Style Classes", 14)); Grid.SetRow(_filter, 1); inventory.Children.Add(_filter); Grid.SetRow(_classes, 2); inventory.Children.Add(_classes); Children.Add(inventory);
        var settings = new ScrollViewer { Content = _form, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _form.Children.Add(ControlTheme.Text("Edit Style", 17));
        _form.Children.Add(new TextBlock { Text = "Leave a field blank to inherit. Changes are staged until Apply or OK.", FontSize = 11, TextWrapping = TextWrapping.Wrap });
        foreach (var group in StyleRules.Properties.GroupBy(p => p.Group))
        {
            var groupBody = new StackPanel { Spacing = 4 };
            var heading = ControlTheme.Button("▾  " + group.Key, () => { });
            heading.FontSize = 12; heading.HorizontalAlignment = HorizontalAlignment.Stretch;
            heading.HorizontalContentAlignment = HorizontalAlignment.Left;
            heading.Padding = new Thickness(6, 4, 6, 4); heading.Background = ControlTheme.Brush("#E2E6E9");
            heading.Margin = new Thickness(0, 8, 0, 0);
            heading.Click += (_, _) =>
            {
                var expanded = groupBody.Visibility == Visibility.Collapsed;
                groupBody.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                heading.Content = (expanded ? "▾  " : "▸  ") + group.Key;
            };
            _form.Children.Add(heading); _form.Children.Add(groupBody); Track("group-" + group.Key, heading);
            foreach (var property in group)
            {
                var row = new Grid { ColumnSpacing = 6 }; row.ColumnDefinitions.Add(new() { Width = new GridLength(94) }); row.ColumnDefinitions.Add(new());
                row.Children.Add(ControlTheme.Text(property.Name, 11));
                var input = new StyleValueEditor(property);
                Grid.SetColumn(input, 1); row.Children.Add(input); groupBody.Children.Add(row); _properties[property.Name] = input;
                Track(property.Name, input.InputElement); input.ValueTextChanged += () => { if (!_sync) ValidatePreview(); };
            }
        }
        Grid.SetColumn(settings, 1); Children.Add(settings);
        var details = new StackPanel { Spacing = 7, Margin = new Thickness(0, 0, 8, 0) };
        details.Children.Add(_name);
        var tools = new Grid { ColumnSpacing = 4, RowSpacing = 4 };
        tools.ColumnDefinitions.Add(new()); tools.ColumnDefinitions.Add(new()); tools.RowDefinitions.Add(new()); tools.RowDefinitions.Add(new());
        void Tool(string id, string label, int row, int column, Action action)
        {
            var button = ControlTheme.Button(label, () => TryEdit(action)); Grid.SetRow(button, row); Grid.SetColumn(button, column); tools.Children.Add(button); Track(id, button);
        }
        Tool("create", "New", 0, 0, () => { CommitCurrent(); _draft.Create(_name.Text); Select(_name.Text); });
        Tool("duplicate", "Duplicate", 0, 1, () => { CommitCurrent(); if (_selected is null) throw new InvalidOperationException("Select a style class first."); _draft.Create(_name.Text, _draft.Properties(_selected)); Select(_name.Text); });
        Tool("rename", "Rename", 1, 0, () => { CommitCurrent(); if (_selected is null) throw new InvalidOperationException("Select a style class first."); _draft.Rename(_selected, _name.Text, _renameReferences.IsChecked == true ? StyleRenameMode.UpdateAssignments : StyleRenameMode.PreserveAssignments); Select(_name.Text); });
        Tool("delete", "Delete", 1, 1, () => { if (_selected is null) return; _draft.Delete(_selected); Select(_draft.Names.FirstOrDefault()); });
        details.Children.Add(tools); details.Children.Add(_renameReferences);
        ToolTipService.SetToolTip(_renameReferences, "Checked: local refactoring convenience. Unchecked: preserve old assignment names, as documented by Perspective; they become missing until reassigned.");
        Track("rename-references", _renameReferences);
        details.Children.Add(_assigned); details.Children.Add(_usage);
        details.Children.Add(ControlTheme.Text("Preview", 14)); _preview.Child = _sample; details.Children.Add(_preview);
        details.Children.Add(new TextBlock { Text = "Preview shows this class alone. Classes apply in alphabetical order; inline properties override them. Deletion also stages removal of class assignments.", FontSize = 11, TextWrapping = TextWrapping.Wrap });
        details.Children.Add(ControlTheme.Text("Applied properties", 12)); details.Children.Add(_applied);
        details.Children.Add(_unknown); var detailScroll = new ScrollViewer { Content = details }; Grid.SetColumn(detailScroll, 2); Children.Add(detailScroll);
        Grid.SetRow(_error, 1); Grid.SetColumnSpan(_error, 3); _error.Margin = new Thickness(8); Children.Add(_error);
        Track("filter", _filter); Track("classes", _classes); Track("name", _name); Track("assigned", _assigned); Track("preview", _preview);
        _assigned.IsEnabled = component is not null;
        _assigned.Checked += (_, _) => { if (!_sync) _assignmentTouched = true; };
        _assigned.Unchecked += (_, _) => { if (!_sync) _assignmentTouched = true; };
        _filter.TextChanged += (_, _) => { if (!_sync) Filter(); };
        _classes.SelectionChanged += (_, _) =>
        {
            if (_sync) return; var next = _classes.SelectedItem as string;
            if (next is null || next == _selected) return;
            if (!TryEdit(() => { CommitCurrent(); Select(next); })) { _sync = true; _classes.SelectedItem = _selected; _sync = false; }
        };
        Select(initialClass is not null && draft.Names.Contains(initialClass) ? initialClass : draft.Names.FirstOrDefault());
    }
    private void Track(string name, FrameworkElement element) { _fields[name] = element; AutomationProperties.SetAutomationId(element, "style-editor-" + name); AutomationProperties.SetName(element, name); }
    public bool TryEdit(Action action)
    {
        try { action(); _error.Text = ""; return true; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException) { _error.Text = ex.Message; return false; }
    }
    private Dictionary<string, Scalar> ReadFields()
    {
        var values = _selected is null ? new Dictionary<string, Scalar>(StringComparer.Ordinal) : new Dictionary<string, Scalar>(_draft.Properties(_selected), StringComparer.Ordinal);
        foreach (var property in StyleRules.Properties)
        {
            var text = _properties[property.Name].Text.Trim();
            if (text.Length == 0) values.Remove(property.Name);
            else values[property.Name] = Scalar.Parse(text, property.Kind);
        }
        StyleRules.ValidateValues(values); return values;
    }
    private void CommitCurrent()
    {
        if (_selected is null) return;
        var values = ReadFields();
        // Validate assignment changes before mutating either part of the detached draft.
        var assignments = _component is not null && _assignmentTouched
            ? _draft.Assignments(_component).Where(n => n != _selected).Concat(_assigned.IsChecked == true ? new[] { _selected } : Array.Empty<string>()).ToArray() : null;
        if (assignments is not null) StyleRules.ValidateAssignments(assignments);
        _draft.Update(_selected, values, assignments is null ? null : _component, assignments);
        _assignmentTouched = false;
    }
    private void Filter()
    {
        _sync = true;
        try
        {
            _classes.ItemsSource = _draft.Names.Where(n => n.Contains(_filter.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
            _classes.SelectedItem = _selected;
        }
        finally { _sync = false; }
    }
    private void Select(string? name)
    {
        _selected = name; _sync = true;
        try
        {
            _filter.Text = ""; _name.Text = name ?? "NewStyle"; _form.IsHitTestVisible = name is not null; _form.Opacity = name is null ? .5 : 1;
            var values = name is null ? new Dictionary<string, Scalar>() : _draft.Properties(name);
            foreach (var (key, input) in _properties)
                input.Text = values.TryGetValue(key, out var value) ? value.Kind == ScalarKind.Number ? NumericInputText.FormatDouble(value.Number) : value.ToString() : "";
            foreach (var input in _properties.Values) input.IsEnabled = name is not null;
            _assigned.IsEnabled = name is not null && _component is not null;
            _assigned.IsChecked = name is not null && _component is not null && _draft.Assignments(_component).Contains(name);
            _usage.Text = name is null ? "Create a class to begin." : _draft.ReferenceCount(name) + " component references · " + _draft.Names.Count + " classes";
            _unknown.Text = "Preserved unsupported properties: " + string.Join(", ", values.Keys.Where(k => !StyleRules.IsSupported(k)));
            _assignmentTouched = false;
        }
        finally { _sync = false; }
        Filter(); ValidatePreview();
    }
    private void ValidatePreview()
    {
        var valid = TryEdit(() =>
        {
            var values = ReadFields();
            if (_previewValues is not null && values.Count == _previewValues.Count && values.All(p => _previewValues.TryGetValue(p.Key, out var v) && p.Value == v)) return;
            _sample.FontSize = 18; _sample.Foreground = ControlTheme.Brush("#354B58"); _sample.FontWeight = FontWeights.Normal; _sample.FontStyle = FontStyle.Normal; _sample.TextAlignment = TextAlignment.Left;
            ComponentStyle.ApplyText(_sample, values); ComponentStyle.ApplyFrame(_preview, values);
            _applied.Text = values.Count == 0 ? "No explicit properties; component defaults apply."
                : string.Join("\n", values.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + ": " +
                    (p.Value.Kind == ScalarKind.Number ? NumericInputText.FormatDouble(p.Value.Number) : p.Value.ToString())));
            _previewValues = values;
        });
        // Do not present the last valid sample as the preview of invalid current input.
        _preview.Visibility = valid ? Visibility.Visible : Visibility.Collapsed;
    }
    public bool Apply(DesignerDocument document) => TryEdit(() => { CommitCurrent(); _draft.Apply(document); Select(_selected); });
}
