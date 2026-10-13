using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using ScadaSpace.Designer;

namespace ScadaSpace.Controls;

public partial class PropertyInspector
{
    private sealed record FilterRow(FrameworkElement Element, string Name, string Path, bool Bound, bool Property)
    { public bool Matches { get; set; } }
    private sealed class FilterSection(string name, Button header)
    {
        public string Name { get; } = name;
        public Button Header { get; } = header;
        public List<FilterRow> Rows { get; } = [];
    }
    public sealed record PropertyCategoryState(bool Expanded, bool Visible, int Matches);
    private readonly PropertyFilter _propertyFilter = new();
    private readonly List<FilterSection> _filterSections = [];
    private readonly HashSet<string> _collapsedCategories = new(StringComparer.Ordinal);
    private readonly HashSet<string> _searchCollapsedCategories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FrameworkElement> _categoryHeaders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FrameworkElement> _qualifiedEditors = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, PropertyCategoryState> _categoryStates = new ReadOnlyDictionary<string, PropertyCategoryState>(new Dictionary<string, PropertyCategoryState>());
    private ReadOnlyDictionary<string, FrameworkElement>? _categoryHeaderView, _qualifiedEditorView;
    private FilterSection? _filterSection;
    private string _filterSectionName = "";
    private readonly TextBox _filterSearch = new() { PlaceholderText = "Filter properties", FontSize = 11, MaxLength = PropertyFilter.MaximumQueryLength, MinWidth = 0, MinHeight = 27, Padding = new Thickness(5, 2, 5, 2) };
    private readonly ToggleButton _filterBound = new() { Content = "●", MinWidth = 26, MinHeight = 27, Padding = new Thickness(3), Foreground = ControlTheme.Brush("#138EB6") };
    private readonly TextBlock _filterCount = ControlTheme.Text("", 10, "#607580");
    private readonly TextBlock _filterProblem = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = ControlTheme.Brush("#A45837"), Visibility = Visibility.Collapsed };
    private readonly TextBlock _filterEmpty = new() { Text = "No matching properties. Clear the filter to show all properties.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(10), Foreground = ControlTheme.Brush("#758792"), Visibility = Visibility.Collapsed };
    private readonly Dictionary<string, FrameworkElement> _filterCommands = new(StringComparer.Ordinal);
    private ReadOnlyDictionary<string, FrameworkElement>? _filterCommandView;
    private IReadOnlyList<string> _visiblePropertyPaths = Array.Empty<string>();
    private IReadOnlyList<string> _matchingPropertyPaths = Array.Empty<string>();
    private bool _filterSync;
    private string _filterError = "";
    public IReadOnlyDictionary<string, FrameworkElement> FilterCommands => _filterCommandView ??= new(_filterCommands);
    public IReadOnlyDictionary<string, FrameworkElement> CategoryHeaders => _categoryHeaderView ??= new(_categoryHeaders);
    public IReadOnlyDictionary<string, FrameworkElement> QualifiedEditorFields => _qualifiedEditorView ??= new(_qualifiedEditors);
    public IReadOnlyDictionary<string, PropertyCategoryState> CategoryStates => _categoryStates;
    public IReadOnlyList<string> VisiblePropertyPaths => _visiblePropertyPaths;
    public IReadOnlyList<string> MatchingPropertyPaths => _matchingPropertyPaths;
    public string FilterQuery => _propertyFilter.Query;
    public bool FilterBoundOnly => _propertyFilter.BoundOnly;
    public PropertySearchSyntax FilterSyntax => _propertyFilter.Syntax;
    public PropertySearchMatch FilterMatchMode => _propertyFilter.MatchMode;
    public bool FilterCaseSensitive => _propertyFilter.CaseSensitive;
    public string FilterError => _filterError;
    public long FilterPatternBuildCount => _propertyFilter.PatternBuildCount;
    public bool FilterHasNoMatches => _filterEmpty.Visibility == Visibility.Visible;
    public string FilterSummary => _filterCount.Text;
    public int PropertyCount { get; private set; }
    public long PropertyRowsCreated { get; private set; }
    public long FilterBuildCount { get; private set; }
    public long FilterApplyCount { get; private set; }
    private bool Searching => _propertyFilter.Query.Length > 0 || _propertyFilter.BoundOnly;
    private HashSet<string> FoldedCategories => Searching ? _searchCollapsedCategories : _collapsedCategories;

    private Grid BuildFilterBar()
    {
        var bar = new Grid { Margin = new Thickness(5, 3, 5, 2), ColumnSpacing = 3 };
        bar.ColumnDefinitions.Add(new());
        for (var i = 0; i < 3; i++) bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        for (var i = 0; i < 3; i++) bar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var clear = ControlTheme.Button("×", ClearPropertyFilter); clear.MinWidth = 26; clear.MinHeight = 27; clear.Padding = new Thickness(3);
        var options = ControlTheme.Button("⌄", () => { }); options.MinWidth = 24; options.MinHeight = 27; options.Padding = new Thickness(2); options.Flyout = BuildSearchOptions();
        AutomationProperties.SetName(_filterSearch, "Filter component properties");
        AutomationProperties.SetName(clear, "Clear property filter");
        AutomationProperties.SetName(_filterBound, "Show only bound properties");
        AutomationProperties.SetName(options, "Property search options");
        ToolTipService.SetToolTip(_filterSearch, "Search property names or qualified paths. Escape clears text and bound-only filtering.");
        ToolTipService.SetToolTip(_filterBound, "Show only properties with configured bindings");
        ToolTipService.SetToolTip(clear, "Clear text and bound-only filters (Escape); retain search options");
        _filterCommands["search"] = _filterSearch; _filterCommands["clear"] = clear; _filterCommands["bound"] = _filterBound; _filterCommands["options"] = options;
        foreach (var (key, value) in _filterCommands) AutomationProperties.SetAutomationId(value, "inspector-filter-" + key);
        Grid.SetColumn(clear, 1); Grid.SetColumn(_filterBound, 2); Grid.SetColumn(options, 3);
        Grid.SetRow(_filterCount, 1); Grid.SetColumnSpan(_filterCount, 4); Grid.SetRow(_filterProblem, 2); Grid.SetColumnSpan(_filterProblem, 4);
        _filterCount.Margin = new Thickness(1, 2, 0, 0);
        bar.Children.Add(_filterSearch); bar.Children.Add(clear); bar.Children.Add(_filterBound); bar.Children.Add(options); bar.Children.Add(_filterCount); bar.Children.Add(_filterProblem);
        _filterSearch.TextChanged += (_, _) => ChangePropertyFilter();
        _filterSearch.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Escape) { ClearPropertyFilter(); e.Handled = true; } };
        _filterBound.Checked += (_, _) => ChangePropertyFilter(); _filterBound.Unchecked += (_, _) => ChangePropertyFilter();
        SynchronizeSearchOptions(); return bar;
    }

    private void ChangePropertyFilter(PropertySearchSyntax? syntax = null, PropertySearchMatch? match = null, bool? sensitive = null)
    {
        if (_filterSync) return;
        if (_propertyFilter.Set(_filterSearch.Text, _filterBound.IsChecked == true,
            syntax ?? FilterSyntax, match ?? FilterMatchMode, sensitive ?? FilterCaseSensitive))
        {
            _searchCollapsedCategories.Clear(); // Reveal the new result set without losing ordinary fold preferences.
            ApplyPropertyFilter();
        }
        SynchronizeSearchOptions();
    }
    private void ClearPropertyFilter()
    {
        _filterSync = true;
        try { _filterSearch.Text = ""; _filterBound.IsChecked = false; }
        finally { _filterSync = false; }
        ChangePropertyFilter(); _filterSearch.Focus(FocusState.Programmatic);
    }
    private void ResetFilterRows()
    {
        _filterSections.Clear(); _categoryHeaders.Clear(); _qualifiedEditors.Clear();
        _filterSection = null; _filterSectionName = ""; PropertyCount = 0;
    }
    private void RegisterPropertyEditor(string name, FrameworkElement element)
    {
        _editorFields[name] = element; // Preserve the legacy unqualified map; qualified consumers cannot lose duplicate names.
        _qualifiedEditors[_filterSectionName + "." + name] = element;
    }
    private void AddFilterRow(FrameworkElement element, string name, bool bound = false, bool property = true)
    {
        _fields.Children.Add(element);
        _filterSection?.Rows.Add(new(element, name, _filterSectionName + "." + name, bound, property));
        if (property) { PropertyCount++; PropertyRowsCreated++; }
    }
    private void FinishFilterRows(bool selected)
    {
        FilterBuildCount++;
        foreach (var control in _filterCommands.Values.OfType<Control>()) control.IsEnabled = selected;
        if (selected) _fields.Children.Add(_filterEmpty);
        ApplyPropertyFilter();
    }
    private void ApplyPropertyFilter()
    {
        FilterApplyCount++; _filterError = _propertyFilter.Error;
        try
        {
            // Stage the complete projection before changing native visibility. A bad
            // pattern/input or timeout must not leave a partially filtered inspector.
            foreach (var section in _filterSections) foreach (var row in section.Rows)
                row.Matches = _filterError.Length == 0 && _propertyFilter.Matches(row.Path, row.Name, row.Bound);
        }
        catch (Exception ex) when (ex is RegexMatchTimeoutException or ArgumentException)
        { _filterError = "Property search stopped: " + ex.Message; }
        if (_filterError.Length > 0)
            foreach (var section in _filterSections) foreach (var row in section.Rows) row.Matches = false;
        _matchingPropertyPaths = _filterSections.SelectMany(s => s.Rows).Where(r => r.Matches && r.Property).Select(r => r.Path).ToList().AsReadOnly();
        _filterProblem.Text = _filterError; _filterProblem.Visibility = _filterError.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetHelpText(_filterSearch, _filterError);
        ApplyCategoryVisibility();
    }
    private void ApplyCategoryVisibility()
    {
        var paths = new List<string>(); var states = new Dictionary<string, PropertyCategoryState>(StringComparer.Ordinal); var any = false;
        foreach (var section in _filterSections)
        {
            var expanded = !FoldedCategories.Contains(section.Name); var matching = false; var count = 0;
            foreach (var row in section.Rows)
            {
                var visible = row.Matches && expanded;
                row.Element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                matching |= row.Matches;
                if (row.Matches && row.Property) count++;
                if (visible && row.Property) paths.Add(row.Path);
            }
            any |= matching;
            section.Header.Content = (expanded ? "▾  " : "▸  ") + section.Name.ToUpperInvariant();
            section.Header.Visibility = matching ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetHelpText(section.Header, expanded ? "Expanded; Left collapses this category" : "Collapsed; Right expands this category");
            states[section.Name] = new(expanded, matching, count);
        }
        _categoryStates = new ReadOnlyDictionary<string, PropertyCategoryState>(states);
        _visiblePropertyPaths = paths.AsReadOnly();
        _filterCount.Text = PropertyCount == 0 ? "No component selected" : paths.Count == _matchingPropertyPaths.Count
            ? $"{paths.Count} of {PropertyCount} properties" : $"{paths.Count} shown · {_matchingPropertyPaths.Count} of {PropertyCount} match";
        _filterEmpty.Visibility = PropertyCount > 0 && !any && _filterError.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Section(string name)
    {
        var key = name.ToLowerInvariant();
        var header = new Button { Content = "▾  " + name, FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = ControlTheme.Brush("#E8ECEF"), Foreground = ControlTheme.Brush("#526671"),
            BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0), MinHeight = 24,
            Padding = new Thickness(7, 3, 5, 3), Margin = new Thickness(0, 5, 0, 0) };
        AutomationProperties.SetName(header, name + " property category"); AutomationProperties.SetAutomationId(header, "inspector-category-" + key);
        void SetExpanded(bool expanded)
        {
            if (!_categoryHeaders.TryGetValue(key, out var current) || !ReferenceEquals(header, current)) return;
            var changed = expanded ? FoldedCategories.Remove(key) : FoldedCategories.Add(key);
            if (changed) ApplyCategoryVisibility(); // Reuse the projection; no predicate evaluation or editor recreation.
        }
        header.Click += (_, _) => SetExpanded(FoldedCategories.Contains(key));
        header.KeyDown += (_, e) => {
            if (e.Key is not (Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right)) return;
            SetExpanded(e.Key == Windows.System.VirtualKey.Right); e.Handled = true;
        };
        _fields.Children.Add(header); _categoryHeaders[key] = header; _filterSectionName = key;
        _filterSection = new FilterSection(key, header); _filterSections.Add(_filterSection);
    }
    private void ExpandCategories(bool expanded)
    {
        var folded = FoldedCategories; var changed = false;
        foreach (var section in _filterSections) changed |= expanded ? folded.Remove(section.Name) : folded.Add(section.Name);
        if (changed) ApplyCategoryVisibility();
    }
}
