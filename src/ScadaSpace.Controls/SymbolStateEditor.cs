using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ScadaSpace.Core;
using ScadaSpace.Designer;
using ScadaSpace.Graphics;

namespace ScadaSpace.Controls;

/// <summary>Native project state editor with detached changes, typed colors, theme resets and retained family previews.</summary>
public sealed partial class SymbolStateEditor : Grid, IDisposable
{
    private readonly ListView _states = new() { FontSize = 12, SelectionMode = ListViewSelectionMode.Single };
    private readonly TextBox _search = new() { PlaceholderText = "Filter states", FontSize = 12, MaxLength = 64 };
    private readonly TextBox _name = new() { Header = "State Name", FontSize = 12, MaxLength = 64 };
    private readonly ComboBox _theme = new() { Header = "Visual Theme", ItemsSource = new[] { "light", "dark" }, SelectedIndex = 0, FontSize = 12, MinWidth = 140 };
    private readonly CheckBox _animate = new() { Content = "Enable Animation", FontSize = 12, MinHeight = 28 };
    private readonly CheckBox _flash = new() { Content = "Enable Flashing", FontSize = 12, MinHeight = 28 };
    private readonly StackPanel _available = new() { Spacing = 1 }, _applied = new() { Spacing = 1 };
    private readonly Dictionary<string, Button> _familyButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StyleValueEditor> _colors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FrameworkElement> _fields = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HmiSymbol> _previews = new(StringComparer.Ordinal);
    private readonly Grid _preview = new() { Height = 125, Padding = new Thickness(4), ColumnSpacing = 5 };
    private readonly TextBlock _error = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = ControlTheme.Brush("#A73828") };
    private readonly TextBlock _notice = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _confirmation = new() { Spacing = 5, Visibility = Visibility.Collapsed };
    private readonly Button _delete;
    private string _selected = "default", _themeName = "light";
    private bool _sync;
    public SymbolStateDraft Draft { get; }
    public string SelectedState => _selected;
    public string ThemeName => _themeName;
    public string ErrorText => _error.Text;
    public bool DeleteConfirmationVisible => _confirmation.Visibility == Visibility.Visible;
    public IReadOnlyDictionary<string, FrameworkElement> Fields { get; }
    public IReadOnlyDictionary<string, HmiSymbol> Previews { get; }
    public IReadOnlyDictionary<string, StyleValueEditor> ColorInputs { get; }
    public SymbolStateEditor(SymbolStateDraft draft)
    {
        Draft = draft ?? throw new ArgumentNullException(nameof(draft));
        Fields = new ReadOnlyDictionary<string, FrameworkElement>(_fields);
        Previews = new ReadOnlyDictionary<string, HmiSymbol>(_previews); ColorInputs = new ReadOnlyDictionary<string, StyleValueEditor>(_colors);
        ColumnDefinitions.Add(new() { Width = new GridLength(190) }); ColumnDefinitions.Add(new()); ColumnSpacing = 12;
        var left = new Grid { Background = ControlTheme.Brush("#E4E8EA"), Padding = new Thickness(8), RowSpacing = 5 };
        left.RowDefinitions.Add(new() { Height = new GridLength(26) }); left.RowDefinitions.Add(new() { Height = new GridLength(34) }); left.RowDefinitions.Add(new()); left.RowDefinitions.Add(new() { Height = GridLength.Auto });
        left.Children.Add(ControlTheme.Text("States", 14)); Grid.SetRow(_search, 1); left.Children.Add(_search);
        Grid.SetRow(_states, 2); left.Children.Add(_states);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        Button Tool(string label, string id, Action action)
        { var b = ControlTheme.Button(label, () => TryEdit(action)); b.FontSize = 12; Track(id, b); tools.Children.Add(b); return b; }
        Tool("+", "new", () => { StageCurrent(); var name = Unique("new-state"); Draft.Create(name); Reload(name); });
        Tool("Duplicate", "duplicate", () => { StageCurrent(); var name = Unique(_selected + "-copy"); Draft.Create(name, _selected); Reload(name); });
        _delete = Tool("×", "delete", () => { StageCurrent(); if (!SymbolStateCatalog.IsBuiltIn(_selected)) _confirmation.Visibility = Visibility.Visible; });
        Grid.SetRow(tools, 3); left.Children.Add(tools); Children.Add(left);
        var right = new StackPanel { Spacing = 8, Padding = new Thickness(2) };
        var identity = new Grid { ColumnSpacing = 8 }; identity.ColumnDefinitions.Add(new()); identity.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        identity.Children.Add(_name); Grid.SetColumn(_theme, 1); identity.Children.Add(_theme); right.Children.Add(identity);
        right.Children.Add(_notice);
        var assignments = new Grid { ColumnSpacing = 15, Height = 126 }; assignments.ColumnDefinitions.Add(new()); assignments.ColumnDefinitions.Add(new());
        StackPanel Section(string title, UIElement content)
        { var panel = new StackPanel { Spacing = 3 }; panel.Children.Add(ControlTheme.Text(title, 12)); panel.Children.Add(content); return panel; }
        var available = Section("Available Symbols", new ScrollViewer { Content = _available, Height = 99 });
        var applied = Section("Applied Symbols", new ScrollViewer { Content = _applied, Height = 99 });
        assignments.Children.Add(available); Grid.SetColumn(applied, 1); assignments.Children.Add(applied); right.Children.Add(assignments);
        foreach (var family in SymbolStateCatalog.Families)
        {
            var button = ControlTheme.Button(family, () => TryEdit(() =>
            {
                StageCurrent(); var state = Draft.Catalog.Get(_selected); var next = state.Symbols.ToList();
                if (!next.Remove(family)) next.Add(family);
                Draft.Set(_selected, state.WithSymbols(next)); LoadFields();
            }));
            button.FontSize = 11; button.Height = 25; button.MinHeight = 0; button.Padding = new Thickness(5, 0, 5, 0);
            button.HorizontalAlignment = HorizontalAlignment.Stretch; _familyButtons.Add(family, button); Track("family-" + family, button);
        }
        right.Children.Add(ControlTheme.Text("Edit State Style", 13));
        var colors = new Grid { ColumnSpacing = 10, RowSpacing = 6 };
        colors.ColumnDefinitions.Add(new()); colors.ColumnDefinitions.Add(new()); colors.RowDefinitions.Add(new()); colors.RowDefinitions.Add(new());
        var specs = new[] { ("primary", "Primary Color"), ("secondary", "Secondary Color"), ("tertiary", "Tertiary Color (vessel liquid)"), ("stroke", "Stroke Color") };
        for (var i = 0; i < specs.Length; i++)
        {
            var (key, label) = specs[i]; var editor = new StyleValueEditor(new("background", "State", ScalarKind.String, "#RRGGBB or #AARRGGBB"));
            editor.AuxiliaryControls["reset"].Visibility = Visibility.Collapsed;
            _colors.Add(key, editor); Track(key, editor.InputElement); AutomationProperties.SetName(editor.InputElement, label);
            var panel = Section(label, editor); Grid.SetColumn(panel, i % 2); Grid.SetRow(panel, i / 2); colors.Children.Add(panel);
            editor.ValueTextChanged += UpdatePreview;
        }
        right.Children.Add(colors);
        var effects = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 }; effects.Children.Add(_animate); effects.Children.Add(_flash); right.Children.Add(effects);
        var resets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        var revert = ControlTheme.Button("Undo Theme", () => TryEdit(() => { Draft.RevertTheme(_selected, _themeName); LoadFields(); }));
        var reset = ControlTheme.Button("Reset Theme", () => TryEdit(() => { Draft.ResetTheme(_selected, _themeName); LoadFields(); }));
        resets.Children.Add(revert); resets.Children.Add(reset); right.Children.Add(resets); Track("revert", revert); Track("reset", reset);
        right.Children.Add(ControlTheme.Text("Preview · effects remain paused while editing", 11));
        var column = 0;
        foreach (var family in SymbolStateCatalog.Families)
        {
            _preview.ColumnDefinitions.Add(new());
            var symbol = new HmiSymbol { Kind = Enum.Parse<SymbolKind>(family), AllowAnimation = false, MinHeight = 0 };
            Grid.SetColumn(symbol, column++); _preview.Children.Add(symbol); _previews.Add(family, symbol);
        }
        right.Children.Add(_preview); right.Children.Add(_error);
        _confirmation.Children.Add(new TextBlock { Text = "Delete this custom state? Symbols still using its name will report an unknown-state diagnostic.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        var confirmButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        var confirm = ControlTheme.Button("Delete state", () => TryEdit(() => { Draft.Delete(_selected); _confirmation.Visibility = Visibility.Collapsed; Reload("default"); }));
        var cancel = ControlTheme.Button("Keep state", () => _confirmation.Visibility = Visibility.Collapsed);
        confirmButtons.Children.Add(confirm); confirmButtons.Children.Add(cancel); _confirmation.Children.Add(confirmButtons); right.Children.Add(_confirmation);
        Track("delete-confirm", confirm); Track("delete-cancel", cancel);
        var scroll = new ScrollViewer { Content = right, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetColumn(scroll, 1); Children.Add(scroll);
        Track("states", _states); Track("search", _search); Track("name", _name); Track("theme", _theme); Track("animation", _animate); Track("flashing", _flash);
        _search.TextChanged += (_, _) => Filter();
        _states.SelectionChanged += (_, _) =>
        {
            if (_sync || _states.SelectedItem is not string next || next == _selected) return;
            if (!TryEdit(() => { StageCurrent(); _selected = next; _confirmation.Visibility = Visibility.Collapsed; LoadFields(); }))
            { _sync = true; _states.SelectedItem = _selected; _sync = false; }
        };
        _theme.SelectionChanged += (_, _) =>
        {
            if (_sync || _theme.SelectedItem is not string next || next == _themeName) return;
            if (!TryEdit(() => { StageCurrent(); _themeName = next; LoadFields(); }))
            { _sync = true; _theme.SelectedItem = _themeName; _sync = false; }
        };
        _animate.Checked += (_, _) => UpdatePreview(); _animate.Unchecked += (_, _) => UpdatePreview();
        _flash.Checked += (_, _) => UpdatePreview(); _flash.Unchecked += (_, _) => UpdatePreview();
        Reload("default");
    }
    private void Track(string id, FrameworkElement element) { _fields[id] = element; AutomationProperties.SetAutomationId(element, "symbol-state-" + id); }
    private string Unique(string prefix)
    {
        prefix = prefix[..Math.Min(50, prefix.Length)]; var name = prefix; var i = 2;
        while (Draft.Names.Contains(name)) name = prefix + "-" + i++;
        return name;
    }
    private void Filter()
    {
        _sync = true;
        try { _states.ItemsSource = Draft.Names.Where(n => n.Contains(_search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray(); _states.SelectedItem = _selected; }
        finally { _sync = false; }
    }
    private void Reload(string selected) { _selected = selected; _sync = true; _search.Text = ""; _sync = false; Filter(); LoadFields(); }
    private void LoadFields()
    {
        var state = Draft.Catalog.Get(_selected); var style = _themeName == "light" ? state.Light : state.Dark;
        _sync = true;
        try
        {
            _name.Text = _selected; _name.IsReadOnly = SymbolStateCatalog.IsBuiltIn(_selected); _delete.IsEnabled = !_name.IsReadOnly; _theme.SelectedItem = _themeName;
            _colors["primary"].Text = style.PrimaryColor; _colors["secondary"].Text = style.SecondaryColor;
            _colors["tertiary"].Text = style.TertiaryColor; _colors["stroke"].Text = style.StrokeColor;
            _animate.IsChecked = style.EnableAnimation; _flash.IsChecked = style.EnableFlashing;
            _notice.Text = _name.IsReadOnly ? "Built-in name is fixed. Select a family to move it between available and applied lists." : "Renames update literal component states; expressions and external values are not rewritten.";
            foreach (var (family, button) in _familyButtons)
            {
                var applied = state.AppliesTo(family); var target = applied ? _applied : _available;
                if (button.Parent is Panel parent && !ReferenceEquals(parent, target)) parent.Children.Remove(button);
                if (button.Parent is null) target.Children.Add(button);
                button.Content = applied ? "← " + family : family + " →";
            }
        }
        finally { _sync = false; }
        UpdatePreview();
    }
    private SymbolStateStyle ReadStyle()
    {
        var style = new SymbolStateStyle(_colors["primary"].Text.Trim(), _colors["secondary"].Text.Trim(), _colors["tertiary"].Text.Trim(), _colors["stroke"].Text.Trim(), _animate.IsChecked == true, _flash.IsChecked == true);
        style.Validate(); return style;
    }
    private void StageCurrent()
    {
        var style = ReadStyle(); var name = _name.Text; SymbolStateCatalog.ValidateName(name);
        if (name != _selected)
        {
            if (SymbolStateCatalog.IsBuiltIn(_selected) || Draft.Names.Contains(name)) throw new ArgumentException("That state name is fixed or already exists.");
            Draft.Rename(_selected, name); _selected = name; Filter();
        }
        Draft.Set(_selected, Draft.Catalog.Get(_selected).WithTheme(_themeName, style));
    }
    private void UpdatePreview()
    {
        if (_sync || _colors.Count != 4) return;
        try
        {
            var style = ReadStyle(); _preview.Visibility = Visibility.Visible; _preview.Background = ControlTheme.Brush(_themeName == "dark" ? "#1C2429" : "#FFFFFF");
            foreach (var (family, symbol) in _previews)
            {
                symbol.Opacity = Draft.Catalog.Get(_selected).AppliesTo(family) ? 1 : .4;
                symbol.SetSnapshot(new(Value: 45, Options: new()
                { State = _selected, StateStyle = style, Appearance = "simple", AnimationSpeed = 0, Orientation = family == "Vessel" ? "vertical" : "right", Label = new(family, FontSize: 10, Color: _themeName == "dark" ? "#E5E9EC" : "#333333") }));
            }
            _error.Text = "";
        }
        catch (ArgumentException error) { _preview.Visibility = Visibility.Collapsed; _error.Text = error.Message; }
    }
    private bool TryEdit(Action action)
    {
        try { action(); _error.Text = ""; return true; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException) { _error.Text = error.Message; return false; }
    }
    public bool Apply(DesignerDocument document) => TryEdit(() => { StageCurrent(); Draft.Apply(document); LoadFields(); });
    public new void Dispose() { foreach (var symbol in _previews.Values) symbol.Dispose(); }
}
