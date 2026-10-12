using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ScadaSpace.Core;
using ScadaSpace.Graphics;

namespace ScadaSpace.Controls;

/// <summary>Reusable native symbol reference gallery. Preview settings never write process values or project content.</summary>
public sealed partial class SymbolLibrary : Grid, IDisposable
{
    private readonly ComboBox _appearance = Choice("Appearance", ["mimic", "simple", "p&id"]);
    private readonly ComboBox _state = Choice("Machine state", ["default", "running", "stopped", "faulted"]);
    private readonly ComboBox _orientation = Choice("Machine orientation", ["right", "bottom", "left", "top"]);
    private readonly ComboBox _vessel = Choice("Vessel orientation", ["vertical", "horizontal"]);
    private readonly HmiNumericEntry _speed = new() { Minimum = 0, Maximum = 100, Value = 50, SmallChange = 5, Height = 32 };
    private readonly CheckBox _reduced = new() { Content = "Reduced motion", MinHeight = 28, FontSize = 12 };
    private readonly Dictionary<string, HmiSymbol> _symbols = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FrameworkElement> _fields = new(StringComparer.Ordinal);
    private bool _sync;
    public IReadOnlyDictionary<string, FrameworkElement> Fields { get; }
    public IReadOnlyDictionary<string, HmiSymbol> Symbols { get; }
    public SymbolDefaults Defaults => new((string)_appearance.SelectedItem, _speed.Value, _reduced.IsChecked == true);
    private static ComboBox Choice(string title, string[] values) => new() { Header = title, ItemsSource = values, SelectedIndex = 0, FontSize = 12, MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    public SymbolLibrary(SymbolDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults); defaults.Validate();
        Fields = new ReadOnlyDictionary<string, FrameworkElement>(_fields); Symbols = new ReadOnlyDictionary<string, HmiSymbol>(_symbols);
        ColumnDefinitions.Add(new() { Width = new GridLength(200) }); ColumnDefinitions.Add(new()); ColumnSpacing = 12;
        var settings = new StackPanel { Spacing = 10, Padding = new Thickness(10), Background = ControlTheme.Brush("#E5E8EA") };
        settings.Children.Add(ControlTheme.Text("SYMBOLS", 13));
        settings.Children.Add(_appearance); settings.Children.Add(_state); settings.Children.Add(_orientation); settings.Children.Add(_vessel);
        settings.Children.Add(ControlTheme.Text("Animation speed (%)", 12)); settings.Children.Add(_speed); settings.Children.Add(_reduced);
        settings.Children.Add(new TextBlock { Text = "Independent vector artwork. Session defaults apply to components using auto. No process values are changed.", TextWrapping = TextWrapping.Wrap, FontSize = 11 });
        Children.Add(new ScrollViewer { Content = settings, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var gallery = new Grid { ColumnSpacing = 8, RowSpacing = 8, Padding = new Thickness(4) };
        for (var i = 0; i < 3; i++) gallery.ColumnDefinitions.Add(new());
        var kinds = new[] { ("Centrifugal pump", SymbolKind.Pump), ("Vacuum pump", SymbolKind.Pump), ("Motor", SymbolKind.Motor), ("Valve", SymbolKind.Valve), ("Sensor", SymbolKind.Sensor), ("Vessel", SymbolKind.Vessel), ("Cylindrical tank", SymbolKind.Tank), ("Gauge", SymbolKind.Gauge), ("Thermometer", SymbolKind.Thermometer) };
        for (var i = 0; i < kinds.Length; i++)
        {
            if (i % 3 == 0) gallery.RowDefinitions.Add(new() { Height = new GridLength(200) });
            var (name, kind) = kinds[i]; var symbol = new HmiSymbol { Kind = kind, MinHeight = 0 };
            AutomationProperties.SetName(symbol, name); _symbols.Add(name, symbol);
            var card = new Grid { Background = ControlTheme.Brush("#FFFFFF"), Padding = new Thickness(5), BorderBrush = ControlTheme.Brush("#CFD5D9"), BorderThickness = new Thickness(1) };
            card.RowDefinitions.Add(new() { Height = new GridLength(24) }); card.RowDefinitions.Add(new());
            card.Children.Add(ControlTheme.Text(name, 12)); Grid.SetRow(symbol, 1); card.Children.Add(symbol);
            Grid.SetColumn(card, i % 3); Grid.SetRow(card, i / 3); gallery.Children.Add(card);
        }
        var scroll = new ScrollViewer { Content = gallery, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetColumn(scroll, 1); Children.Add(scroll);
        foreach (var (key, control) in new (string, FrameworkElement)[] { ("appearance", _appearance), ("state", _state), ("orientation", _orientation), ("vessel", _vessel), ("speed", _speed), ("reduced", _reduced) })
        { _fields.Add(key, control); AutomationProperties.SetAutomationId(control, "symbol-library-" + key); }
        _sync = true; _appearance.SelectedItem = defaults.AutoAppearance; _speed.Value = defaults.AutoAnimationSpeed; _reduced.IsChecked = defaults.ReducedMotion; _sync = false;
        foreach (var combo in new[] { _appearance, _state, _orientation, _vessel }) combo.SelectionChanged += (_, _) => Refresh();
        _speed.ValueChanged += (_, _) => Refresh(); _reduced.Checked += (_, _) => Refresh(); _reduced.Unchecked += (_, _) => Refresh(); Refresh();
    }
    private void Refresh()
    {
        if (_sync) return;
        var state = (string)_state.SelectedItem; var appearance = (string)_appearance.SelectedItem;
        foreach (var (name, symbol) in _symbols)
        {
            SymbolOptions? options = null;
            if (IndustrialSymbolRenderer.Supports(symbol.Kind)) options = new()
            {
                Appearance = appearance, State = symbol.Kind == SymbolKind.Valve ? state switch { "running" => "open", "stopped" => "closed", "faulted" => "failedToOpen", _ => "default" } : state,
                Orientation = symbol.Kind == SymbolKind.Vessel ? (string)_vessel.SelectedItem : (string)_orientation.SelectedItem,
                Variant = name == "Vacuum pump" ? "vacuum" : "centrifugal", AnimationSpeed = _reduced.IsChecked == true ? 0 : _speed.Value
            };
            symbol.SetSnapshot(new(Value: 45, Options: options, Tank: symbol.Kind == SymbolKind.Tank ? new(ValueFormat: "Percent", ShowScale: false) : null));
        }
    }
    public new void Dispose() { foreach (var symbol in _symbols.Values) symbol.Dispose(); }
}
