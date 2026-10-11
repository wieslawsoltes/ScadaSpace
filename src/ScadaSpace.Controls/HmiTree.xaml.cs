using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ScadaSpace.Core;
using ScadaSpace.Data;
using Windows.System;

namespace ScadaSpace.Controls;

public sealed record TreeSelectionRequest(IReadOnlyList<string> Paths, string CurrentPath, Scalar Data);
public sealed record TreeRowViewModel(DocumentTreeRow Row, double Height)
{
    public string Path => Row.Item.Path;
    public string Label => Row.Item.Label;
    public string Chevron => Row.Expanded ? "⌄" : "›";
    public string ToggleName => (Row.Expanded ? "Collapse " : "Expand ") + Label;
    public string Icon => Row.Item.Icon.Length > 0 ? Row.Item.Icon : Row.Item.Children.Count > 0 ? Row.Expanded ? "▰" : "▱" : "•";
    public Thickness Indent => new(Row.Item.Depth * 18, 0, 0, 0);
    public Visibility ChevronVisibility => Row.Item.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>Virtualized hierarchical control. Only flat visible rows are materialized by Uno's ItemsStackPanel.</summary>
public sealed partial class HmiTree : UserControl
{
    private DocumentTreeState _state = new(new(Scalar.Array([])));
    private List<TreeRowViewModel> _rows = [];
    private bool _sync;
    private double _rowHeight = 28;
    public Func<TreeSelectionRequest, bool>? SelectionRequested { get; set; }
    public event Action<DocumentTreeItem>? ItemInvoked;
    public event Action<string, bool>? ExpansionChanged;
    public event Action<string>? FilterChanged;
    public IReadOnlyList<DocumentTreeRow> VisibleRows => _rows.Select(r => r.Row).ToArray();
    public IReadOnlyList<string> SelectedPaths => _state.Selected.Select(n => n.Path).ToArray();
    public string CurrentPath => _state.CurrentPath;
    public Scalar SelectionData => _state.SelectionData;
    public IReadOnlyList<HmiTreeListItem> RealizedRows => RowsList.RealizedRows;
    public int ItemCount => _state.Tree.Items.Count;
    public long RebuildCount { get; private set; }
    public string Filter
    {
        get => FilterBox.Text;
        set { if (value is null || value.Length > 1024) throw new ArgumentException("Invalid tree filter."); if (FilterBox.Text != value) { _sync = true; try { FilterBox.Text = value; } finally { _sync = false; } Rebuild(); } }
    }
    public bool ShowFilter { get => FilterBox.Visibility == Visibility.Visible; set => FilterBox.Visibility = value ? Visibility.Visible : Visibility.Collapsed; }
    public double RowHeight
    {
        get => _rowHeight;
        set { if (!double.IsFinite(value) || value < 20 || value > 96) throw new ArgumentOutOfRangeException(nameof(value)); if (_rowHeight != value) { _rowHeight = value; Rebuild(); } }
    }
    public bool MultiSelect
    {
        get => RowsList.SelectionMode == ListViewSelectionMode.Extended;
        set
        {
            if (value == MultiSelect) return;
            _sync = true;
            try { RowsList.SelectionMode = value ? ListViewSelectionMode.Extended : ListViewSelectionMode.Single; if (!value && _state.Selected.Count > 1) _state.Select(_state.CurrentPath.Length > 0 ? [_state.CurrentPath] : []); SyncSelection(); }
            finally { _sync = false; }
        }
    }
    public HmiTree()
    {
        InitializeComponent(); RowsList.TreeOwner = this; FilterBox.MaxLength = 1024;
    }
    public void SetItems(Scalar items)
    {
        if (_state.Tree.Source == items) return;
        var tree = new DocumentTree(items);
        _state.Replace(tree); Rebuild();
    }
    public void SelectPaths(IEnumerable<string> paths, bool reveal = true)
    {
        var values = paths.Take(DocumentTree.MaximumItems + 1).ToArray(); if (!MultiSelect && values.Length > 1) throw new ArgumentException("Multiple paths require multiSelect.");
        _state.Select(values);
        if (reveal) foreach (var path in values) _state.Reveal(path);
        Rebuild();
    }
    public void SetExpanded(string path, bool expanded)
    {
        _state.SetExpanded(path, expanded); Rebuild(); ExpansionChanged?.Invoke(path, expanded);
    }
    public void ExpandAll(bool expanded) { _state.ExpandAll(expanded); Rebuild(); }
    private void Rebuild()
    {
        _sync = true;
        try
        {
            _rows = _state.Visible(FilterBox.Text).Select(row => new TreeRowViewModel(row, _rowHeight)).ToList();
            RowsList.ResetPendingFocus(); RowsList.ItemsSource = _rows; SyncSelection(); RebuildCount++;
            StatusText.Text = $"{_rows.Count} visible / {_state.Tree.Items.Count} items · {_state.Selected.Count} selected";
        }
        finally { _sync = false; }
    }
    private void SyncSelection()
    {
        var keys = _state.Selected.Select(n => n.Key).ToHashSet(StringComparer.Ordinal);
        RowsList.SelectedItems.Clear();
        if (MultiSelect) foreach (var row in _rows.Where(r => keys.Contains(r.Row.Item.Key))) RowsList.SelectedItems.Add(row);
        else RowsList.SelectedItem = _rows.FirstOrDefault(r => keys.Contains(r.Row.Item.Key));
    }
    private void FilterTextChanged(object sender, TextChangedEventArgs e)
    { if (!_sync && RowsList is not null) { Rebuild(); FilterChanged?.Invoke(FilterBox.Text); } }
    private void ToggleClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path } && IsEnabled) SetExpanded(path, !_state.IsExpanded(_state.Tree.Get(path)));
    }
    private void ItemClicked(object sender, ItemClickEventArgs e)
    { if (!_sync && IsEnabled && e.ClickedItem is TreeRowViewModel row) ItemInvoked?.Invoke(row.Row.Item); }
    private void SelectedItemsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_sync || !IsEnabled) return;
        var items = RowsList.SelectedItems.OfType<TreeRowViewModel>().ToArray();
        var paths = items.Select(r => r.Path).ToArray();
        var current = e.AddedItems.OfType<TreeRowViewModel>().LastOrDefault()?.Path ?? items.LastOrDefault()?.Path ?? "";
        var accepted = false;
        try
        {
            var candidate = new DocumentTreeState(_state.Tree); candidate.Select(paths, current.Length == 0 ? null : current);
            accepted = SelectionRequested?.Invoke(new(paths, current, candidate.SelectionData)) ?? true;
            if (accepted) _state.Select(paths, current.Length == 0 ? null : current);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { SetDataError(ex.Message); }
        if (!accepted) { _sync = true; try { SyncSelection(); } finally { _sync = false; } }
        if (DataError is null) StatusText.Text = $"{_rows.Count} visible / {_state.Tree.Items.Count} items · {_state.Selected.Count} selected";
    }
    internal bool HandleTreeKey(KeyRoutedEventArgs e)
    {
        if (!IsEnabled || _rows.Count == 0 || e.Key is not (VirtualKey.Left or VirtualKey.Right or VirtualKey.Home or VirtualKey.End)) return false;
        if (e.Key is VirtualKey.Home or VirtualKey.End)
        {
            var endpoint = e.Key == VirtualKey.Home ? _rows[0] : _rows[^1];
            RowsList.SelectedItem = endpoint; RowsList.FocusRow(endpoint); return true;
        }
        var row = RowsList.FocusedItem?.Row ?? _rows.FirstOrDefault(r => r.Path == _state.CurrentPath) ?? _rows[0];
        var item = row.Row.Item; string? select = null;
        if (e.Key == VirtualKey.Right)
        {
            if (item.Children.Count > 0 && !row.Row.Expanded) SetExpanded(item.Path, true);
            else if (item.Children.Count > 0) select = _rows.FirstOrDefault(r => r.Row.Item.ParentPath == item.Path)?.Path;
        }
        else if (row.Row.Expanded && item.Children.Count > 0) SetExpanded(item.Path, false);
        else select = item.ParentPath;
        var focusPath = select ?? item.Path;
        if (select is not null) RowsList.SelectedItem = _rows.FirstOrDefault(r => r.Path == select);
        var target = _rows.FirstOrDefault(r => r.Path == focusPath);
        if (target is not null) RowsList.FocusRow(target);
        return true;
    }
}
