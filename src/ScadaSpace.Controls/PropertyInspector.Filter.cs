using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using ScadaSpace.Designer;

namespace ScadaSpace.Controls;

public partial class PropertyInspector
{
    private sealed record FilterRow(FrameworkElement Element, string Path, bool Bound, bool Property);
    private sealed class FilterSection(Border header)
    {
        public Border Header { get; } = header;
        public List<FilterRow> Rows { get; } = [];
    }
    private readonly PropertyFilter _propertyFilter = new();
    private readonly List<FilterSection> _filterSections = [];
    private FilterSection? _filterSection;
    private string _filterSectionName = "";
    private readonly TextBox _filterSearch = new() { PlaceholderText = "Filter properties", FontSize = 11, MaxLength = PropertyFilter.MaximumQueryLength, MinWidth = 0, MinHeight = 27, Padding = new Thickness(5, 2, 5, 2) };
    private readonly ToggleButton _filterBound = new() { Content = "●", MinWidth = 26, MinHeight = 27, Padding = new Thickness(3), Foreground = ControlTheme.Brush("#138EB6") };
    private readonly TextBlock _filterCount = ControlTheme.Text("", 10, "#607580");
    private readonly TextBlock _filterEmpty = new() { Text = "No matching properties. Clear the filter to show all properties.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(10), Foreground = ControlTheme.Brush("#758792"), Visibility = Visibility.Collapsed };
    private readonly Dictionary<string, FrameworkElement> _filterCommands = new(StringComparer.Ordinal);
    private ReadOnlyDictionary<string, FrameworkElement>? _filterCommandView;
    private IReadOnlyList<string> _visiblePropertyPaths = Array.Empty<string>();
    private Grid? _filterBar;
    private bool _filterSync;
    public IReadOnlyDictionary<string, FrameworkElement> FilterCommands => _filterCommandView ??= new(_filterCommands);
    public IReadOnlyList<string> VisiblePropertyPaths => _visiblePropertyPaths;
    public string FilterQuery => _propertyFilter.Query;
    public bool FilterBoundOnly => _propertyFilter.BoundOnly;
    public bool FilterHasNoMatches => _filterEmpty.Visibility == Visibility.Visible;
    public string FilterSummary => _filterCount.Text;
    public int PropertyCount { get; private set; }
    public long PropertyRowsCreated { get; private set; }
    public long FilterBuildCount { get; private set; }
    public long FilterApplyCount { get; private set; }

    private Grid BuildFilterBar()
    {
        var bar = new Grid { Margin = new Thickness(5, 3, 5, 2), ColumnSpacing = 3 };
        bar.ColumnDefinitions.Add(new()); bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        bar.RowDefinitions.Add(new() { Height = GridLength.Auto }); bar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var clear = ControlTheme.Button("×", ClearPropertyFilter); clear.MinWidth = 26; clear.MinHeight = 27; clear.Padding = new Thickness(3);
        AutomationProperties.SetName(_filterSearch, "Filter component properties");
        AutomationProperties.SetName(clear, "Clear property filter");
        AutomationProperties.SetName(_filterBound, "Show only bound properties");
        ToolTipService.SetToolTip(_filterSearch, "Search literal property names or paths, such as props.color. Escape clears both filters.");
        ToolTipService.SetToolTip(_filterBound, "Show only properties with configured bindings");
        ToolTipService.SetToolTip(clear, "Clear text and bound-only filters (Escape)");
        _filterCommands["search"] = _filterSearch; _filterCommands["clear"] = clear; _filterCommands["bound"] = _filterBound;
        foreach (var (key, value) in _filterCommands) AutomationProperties.SetAutomationId(value, "inspector-filter-" + key);
        Grid.SetColumn(clear, 1); Grid.SetColumn(_filterBound, 2); Grid.SetRow(_filterCount, 1); Grid.SetColumnSpan(_filterCount, 3);
        _filterCount.Margin = new Thickness(1, 2, 0, 0);
        bar.Children.Add(_filterSearch); bar.Children.Add(clear); bar.Children.Add(_filterBound); bar.Children.Add(_filterCount);
        _filterSearch.TextChanged += (_, _) => ChangePropertyFilter();
        _filterSearch.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Escape) { ClearPropertyFilter(); e.Handled = true; } };
        _filterBound.Checked += (_, _) => ChangePropertyFilter(); _filterBound.Unchecked += (_, _) => ChangePropertyFilter();
        return _filterBar = bar;
    }

    private void ChangePropertyFilter()
    {
        if (!_filterSync && _propertyFilter.Set(_filterSearch.Text, _filterBound.IsChecked == true)) ApplyPropertyFilter();
    }
    private void ClearPropertyFilter()
    {
        _filterSync = true;
        try { _filterSearch.Text = ""; _filterBound.IsChecked = false; }
        finally { _filterSync = false; }
        if (_propertyFilter.Set("")) ApplyPropertyFilter();
        _filterSearch.Focus(FocusState.Programmatic);
    }
    private void ResetFilterRows()
    {
        _filterSections.Clear(); _filterSection = null; _filterSectionName = ""; PropertyCount = 0;
    }
    private void AddFilterRow(FrameworkElement element, string name, bool bound = false, bool property = true)
    {
        _fields.Children.Add(element);
        _filterSection?.Rows.Add(new(element, _filterSectionName + "." + name, bound, property));
        if (property) { PropertyCount++; PropertyRowsCreated++; }
    }
    private void FinishFilterRows(bool selected)
    {
        FilterBuildCount++;
        if (_filterBar is not null) _filterBar.IsEnabled = selected;
        if (selected) _fields.Children.Add(_filterEmpty);
        ApplyPropertyFilter();
    }
    private void ApplyPropertyFilter()
    {
        FilterApplyCount++;
        var paths = new List<string>(); var any = false;
        foreach (var section in _filterSections)
        {
            var visibleSection = false;
            foreach (var row in section.Rows)
            {
                var visible = _propertyFilter.Matches(row.Path, row.Bound);
                row.Element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                if (!visible) continue;
                visibleSection = true; any = true;
                if (row.Property) paths.Add(row.Path);
            }
            section.Header.Visibility = visibleSection ? Visibility.Visible : Visibility.Collapsed;
        }
        _visiblePropertyPaths = paths.AsReadOnly();
        _filterCount.Text = PropertyCount == 0 ? "No component selected" : $"{paths.Count} of {PropertyCount} properties";
        _filterEmpty.Visibility = PropertyCount > 0 && !any ? Visibility.Visible : Visibility.Collapsed;
    }
}
