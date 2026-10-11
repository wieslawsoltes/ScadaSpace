using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ScadaSpace.Designer;

namespace ScadaSpace.Controls;

/// <summary>Virtualized native configuration inventory with cached search and owned details.</summary>
public sealed partial class ConfigurationExplorer : Grid
{
    private readonly ConfigurationIndex _index;
    private readonly TextBox _search = new() { PlaceholderText = "Search views, components, properties and sources", MaxLength = 256, FontSize = 12 };
    private readonly ComboBox _kind = new() { ItemsSource = new[] { "All configurations" }.Concat(Enum.GetNames<ConfigurationKind>()).ToArray(), SelectedIndex = 0, MinWidth = 150, FontSize = 12 };
    private readonly ListView _list = new() { SelectionMode = ListViewSelectionMode.Single, FontSize = 12 };
    private readonly TextBox _details = new() { IsReadOnly = true, AcceptsReturn = true, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _count = ControlTheme.Text("", 11);
    private IReadOnlyList<ConfigurationReference> _rows = Array.Empty<ConfigurationReference>();
    public ConfigurationReference? Selected => _list.SelectedItem as ConfigurationReference;
    public IReadOnlyList<ConfigurationReference> Rows => _rows;
    public IReadOnlyDictionary<string, FrameworkElement> EditorFields { get; }
    public ConfigurationExplorer(ConfigurationIndex index)
    {
        _index = index;
        RowDefinitions.Add(new() { Height = new GridLength(39) }); RowDefinitions.Add(new()); RowDefinitions.Add(new() { Height = new GridLength(150) }); RowDefinitions.Add(new() { Height = new GridLength(24) });
        var top = new Grid { ColumnSpacing = 8 }; top.ColumnDefinitions.Add(new()); top.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); top.Children.Add(_search); Grid.SetColumn(_kind, 1); top.Children.Add(_kind); Children.Add(top);
        Grid.SetRow(_list, 1); Children.Add(_list); Grid.SetRow(_details, 2); Children.Add(_details); Grid.SetRow(_count, 3); Children.Add(_count);
        EditorFields = new Dictionary<string, FrameworkElement> { ["search"] = _search, ["kind"] = _kind, ["list"] = _list, ["details"] = _details };
        foreach (var (key, element) in EditorFields) AutomationProperties.SetAutomationId(element, "configuration-explorer-" + key);
        _search.TextChanged += (_, _) => Filter(); _kind.SelectionChanged += (_, _) => Filter();
        _list.SelectionChanged += (_, _) => _details.Text = Selected is { } item ? $"{item.View} / {item.ComponentName}\n{item.Kind} · {item.Location}\n\n{item.Source}" : "Select a configuration to inspect its source.";
        Filter();
    }
    private void Filter()
    {
        var next = _index.Query(_search.Text, _kind.SelectedIndex > 0 ? (ConfigurationKind)(_kind.SelectedIndex - 1) : null);
        if (ReferenceEquals(next, _rows)) return;
        var selected = Selected; _rows = next; _list.ItemsSource = _rows;
        _list.SelectedItem = selected is not null && _rows.Contains(selected) ? selected : _rows.FirstOrDefault();
        _count.Text = $"{_rows.Count} matching configurations / {_index.Entries.Count} total · Go To Reference selects the component";
    }
}
