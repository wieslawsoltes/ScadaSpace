using System.Collections.ObjectModel;
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
    private ObservableCollection<TreeRowViewModel> _rows = [];
    private IReadOnlyList<DocumentTreeRow>? _projection;
    private double _projectedHeight;
    private string _filterText = "";
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
        get => _filterText;
        set
        {
            if (value is null || value.Length > 1024) throw new ArgumentException("Invalid tree filter.");
            // Compare with the last committed value, not the editor's in-flight text.
            // TextChanged is asynchronous; polling must not restore the old model value
            // between keyboard deletion and its notification.
            if (_filterText == value) return;
            _filterText = value;
            var wasSync = _sync; _sync = true;
            try { FilterBox.Text = value; } finally { _sync = wasSync; }
            Rebuild();
        }
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
        InitializeComponent(); RowsList.TreeOwner = this; FilterBox.MaxLength = 1024; RowsList.ItemsSource = _rows;
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
        var wasSync = _sync; _sync = true;
        try
        {
            var projection = _state.Visible(_filterText);
            if (!ReferenceEquals(projection, _projection) || _rowHeight != _projectedHeight)
            {
                var old = _rows.ToDictionary(r => r.Row.Item.Key, StringComparer.Ordinal);
                var next = projection.Select(row => old.TryGetValue(row.Item.Key, out var prior) && prior.Row == row && prior.Height == _rowHeight ? prior : new TreeRowViewModel(row, _rowHeight)).ToArray();
                var prefix = 0;
                while (prefix < _rows.Count && prefix < next.Length && ReferenceEquals(_rows[prefix], next[prefix])) prefix++;
                var suffix = 0;
                while (suffix < _rows.Count - prefix && suffix < next.Length - prefix && ReferenceEquals(_rows[_rows.Count - 1 - suffix], next[next.Length - 1 - suffix])) suffix++;
                var remove = _rows.Count - prefix - suffix; var insert = next.Length - prefix - suffix;
                RowsList.ResetPendingFocus();
                if (remove + insert <= 64)
                {
                    // Small branch edits preserve unrelated native containers and scroll position.
                    for (var i = 0; i < remove; i++) _rows.RemoveAt(prefix);
                    for (var i = 0; i < insert; i++) _rows.Insert(prefix + i, next[prefix + i]);
                }
                else { _rows = new(next); RowsList.ItemsSource = _rows; }
                _projection = projection; _projectedHeight = _rowHeight; RebuildCount++;
            }
            SyncSelection();
            StatusText.Text = $"{_rows.Count} visible / {_state.Tree.Items.Count} items · {_state.Selected.Count} selected";
        }
        finally { _sync = wasSync; }
    }
    private void SyncSelection()
    {
        var keys = _state.Selected.Select(n => n.Key).ToHashSet(StringComparer.Ordinal);
        RowsList.SelectedItems.Clear();
        if (MultiSelect) foreach (var row in _rows.Where(r => keys.Contains(r.Row.Item.Key))) RowsList.SelectedItems.Add(row);
        else RowsList.SelectedItem = _rows.FirstOrDefault(r => keys.Contains(r.Row.Item.Key));
    }
    private void FilterTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_sync || RowsList is null || _filterText == FilterBox.Text) return;
        _filterText = FilterBox.Text; Rebuild(); FilterChanged?.Invoke(_filterText);
    }
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
