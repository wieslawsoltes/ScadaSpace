using System.Collections.ObjectModel;
using System.Globalization;
using ScadaSpace.Core;

namespace ScadaSpace.Data;

/// <summary>Immutable item snapshot. Index paths follow input order; optional IDs preserve identity across reordering.</summary>
public sealed record DocumentTreeItem(string Path, string Key, string Label, Scalar Data, bool InitiallyExpanded,
    string Icon, int Depth, int Index, string? ParentPath, IReadOnlyList<DocumentTreeItem> Children)
{
    public Scalar IndexPath => Scalar.Array(Path.Split('/').Select(p => Scalar.From(int.Parse(p, CultureInfo.InvariantCulture))));
    public Scalar EventData => Scalar.Object(new Dictionary<string, Scalar> { ["itemPath"] = IndexPath, ["label"] = Scalar.From(Label), ["data"] = Data });
}

/// <summary>Validated array-of-objects tree, independent of Uno and process providers.</summary>
public sealed class DocumentTree
{
    public const int MaximumItems = 4096;
    public Scalar Source { get; }
    public IReadOnlyList<DocumentTreeItem> Roots { get; }
    public IReadOnlyList<DocumentTreeItem> Items { get; }
    public IReadOnlyDictionary<string, DocumentTreeItem> ByPath { get; }
    public IReadOnlyDictionary<string, DocumentTreeItem> ByKey { get; }
    private readonly Dictionary<string, int> _order = new(StringComparer.Ordinal);
    internal int Ordinal(DocumentTreeItem item) => _order[item.Key];
    public DocumentTree(Scalar source)
    {
        if (!source.IsValid() || source.Kind != ScalarKind.Array) throw new ArgumentException("Tree items must be an array.");
        Source = source;
        var paths = new Dictionary<string, DocumentTreeItem>(StringComparer.Ordinal);
        var keys = new Dictionary<string, DocumentTreeItem>(StringComparer.Ordinal);
        var ordered = new List<DocumentTreeItem>(); var count = 0;
        IReadOnlyList<DocumentTreeItem> Read(Scalar array, string? parent, int depth)
        {
            if (array.Kind != ScalarKind.Array || depth > 24) throw new ArgumentException("Invalid child array or excessive tree depth.");
            var result = new List<DocumentTreeItem>();
            for (var i = 0; i < array.Items.Count; i++)
            {
                if (++count > MaximumItems) throw new ArgumentException("Tree exceeds 4096 items.");
                var value = array.Items[i];
                if (value.Kind != ScalarKind.Object) throw new ArgumentException("Tree items must be objects.");
                var fields = value.Fields;
                var label = fields.GetValueOrDefault("label", Scalar.From(""));
                var expanded = fields.GetValueOrDefault("expanded", Scalar.From(false));
                var icon = fields.GetValueOrDefault("icon", Scalar.From(""));
                var id = fields.GetValueOrDefault("id");
                if (label.Kind != ScalarKind.String || label.Text!.Length > 4096 || expanded.Kind != ScalarKind.Boolean || icon.Kind != ScalarKind.String || icon.Text!.Length > 64 || id.Kind is not (ScalarKind.String or ScalarKind.Null) || id.Text?.Length > 512 || id.Kind == ScalarKind.String && string.IsNullOrEmpty(id.Text)) throw new ArgumentException("Invalid tree label, expansion, icon or identity.");
                var path = parent is null ? i.ToString(CultureInfo.InvariantCulture) : parent + "/" + i.ToString(CultureInfo.InvariantCulture);
                var children = Read(fields.GetValueOrDefault("items", Scalar.Array([])), path, depth + 1);
                var item = new DocumentTreeItem(path, id.Kind == ScalarKind.String ? "id:" + id.Text : "path:" + path, label.Text!, fields.GetValueOrDefault("data"), expanded.Boolean, icon.Text!, depth, i, parent, children);
                if (!keys.TryAdd(item.Key, item)) throw new ArgumentException("Duplicate tree ID: " + id.Text);
                paths.Add(path, item); result.Add(item);
            }
            return result.AsReadOnly();
        }
        Roots = Read(source, null, 0);
        void Walk(IEnumerable<DocumentTreeItem> items) { foreach (var item in items) { ordered.Add(item); Walk(item.Children); } }
        Walk(Roots); Items = ordered.AsReadOnly();
        for (var i = 0; i < ordered.Count; i++) _order.Add(ordered[i].Key, i);
        ByPath = new ReadOnlyDictionary<string, DocumentTreeItem>(paths); ByKey = new ReadOnlyDictionary<string, DocumentTreeItem>(keys);
    }
    public DocumentTreeItem Get(string path) => ByPath.TryGetValue(path, out var item) ? item : throw new ArgumentException("Unknown tree index path: " + path);
}

public sealed record DocumentTreeRow(DocumentTreeItem Item, bool Expanded);

/// <summary>View-local expansion and selection over immutable data. Filtering never changes expansion defaults.</summary>
public sealed class DocumentTreeState
{
    private readonly Dictionary<string, bool> _expansion = new(StringComparer.Ordinal);
    private readonly HashSet<string> _selection = new(StringComparer.Ordinal);
    private string? _current;
    public DocumentTree Tree { get; private set; }
    public DocumentTreeState(DocumentTree tree) => Tree = tree ?? throw new ArgumentNullException(nameof(tree));
    private IReadOnlyList<DocumentTreeItem> _selected = Array.Empty<DocumentTreeItem>();
    private IReadOnlyList<DocumentTreeRow>? _visible;
    private string? _filter;
    public IReadOnlyList<DocumentTreeItem> Selected => _selected;
    public long ProjectionBuildCount { get; private set; }
    private static IReadOnlyList<DocumentTreeItem> OrderedSelection(DocumentTree tree, IEnumerable<string> keys) =>
        Array.AsReadOnly(keys.Where(tree.ByKey.ContainsKey).Select(key => tree.ByKey[key]).OrderBy(tree.Ordinal).ToArray());
    public string CurrentPath => _current is not null && Tree.ByKey.TryGetValue(_current, out var item) ? item.Path : "";
    public Scalar SelectionData { get; private set; } = Scalar.Array([]);
    private static Scalar SelectionSnapshot(IEnumerable<DocumentTreeItem> items) => Scalar.Array(items.Select(n => Scalar.Object(new Dictionary<string, Scalar> { ["itemPath"] = n.IndexPath, ["value"] = n.Data })));
    public bool IsExpanded(DocumentTreeItem item) => item.Children.Count > 0 && _expansion.GetValueOrDefault(item.Key, item.InitiallyExpanded);
    public void Replace(DocumentTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (ReferenceEquals(Tree, tree)) return;
        var selected = OrderedSelection(tree, _selection);
        var data = SelectionSnapshot(selected);
        foreach (var key in _expansion.Keys.ToArray())
            if (!tree.ByKey.TryGetValue(key, out var item) || !Tree.ByKey.TryGetValue(key, out var old) || old.InitiallyExpanded != item.InitiallyExpanded) _expansion.Remove(key);
        _selection.RemoveWhere(key => !tree.ByKey.ContainsKey(key));
        if (_current is not null && !tree.ByKey.ContainsKey(_current)) _current = tree.Items.FirstOrDefault(n => _selection.Contains(n.Key))?.Key;
        Tree = tree; _selected = selected; SelectionData = data; _visible = null;
    }
    public void SetExpanded(string path, bool expanded, bool recursive = false)
    {
        void Apply(DocumentTreeItem item)
        {
            if (item.Children.Count > 0)
            {
                if (IsExpanded(item) != expanded) _visible = null;
                _expansion[item.Key] = expanded;
            }
            if (recursive) foreach (var child in item.Children) Apply(child);
        }
        Apply(Tree.Get(path));
    }
    public void ExpandAll(bool expanded)
    { foreach (var item in Tree.Roots) SetExpanded(item.Path, expanded, true); }
    public void Reveal(string path)
    {
        var item = Tree.Get(path);
        while (item.ParentPath is { } parent)
        {
            item = Tree.Get(parent);
            if (!IsExpanded(item)) _visible = null;
            _expansion[item.Key] = true;
        }
    }
    public void Select(IEnumerable<string> paths, string? current = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var selected = new HashSet<string>(StringComparer.Ordinal); DocumentTreeItem? last = null; var count = 0;
        foreach (var path in paths)
        {
            if (++count > DocumentTree.MaximumItems) throw new ArgumentException("Too many selected paths.");
            last = Tree.Get(path); selected.Add(last.Key);
        }
        var active = current is null ? last : Tree.Get(current);
        if (active is not null && !selected.Contains(active.Key)) throw new ArgumentException("Current tree item must be selected.");
        // Selection output has its own structured-value budget. Validate it before mutating state.
        if (_selection.SetEquals(selected)) { _current = active?.Key; return; }
        var ordered = OrderedSelection(Tree, selected);
        var data = SelectionSnapshot(ordered);
        _selection.Clear(); _selection.UnionWith(selected); _current = active?.Key; _selected = ordered; SelectionData = data;
    }
    public IReadOnlyList<DocumentTreeRow> Visible(string filter = "")
    {
        if (filter is null || filter.Length > 1024) throw new ArgumentException("Tree filter exceeds 1024 characters.");
        filter = string.IsNullOrWhiteSpace(filter) ? "" : filter;
        if (_visible is not null && _filter == filter) return _visible;
        return BuildProjection(filter);
    }
    // Keep the cache hit outside the closure-bearing traversal so it allocates nothing.
    private IReadOnlyList<DocumentTreeRow> BuildProjection(string filter)
    {
        var rows = new List<DocumentTreeRow>();
        HashSet<string>? matches = null;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            matches = new(StringComparer.Ordinal);
            foreach (var item in Tree.Items)
                if (item.Label.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    var current = item; matches.Add(current.Key);
                    while (current.ParentPath is { } parent) { current = Tree.Get(parent); matches.Add(current.Key); }
                }
        }
        void Walk(IEnumerable<DocumentTreeItem> items)
        {
            foreach (var item in items)
            {
                if (matches is not null && !matches.Contains(item.Key)) continue;
                var expanded = matches is null ? IsExpanded(item) : item.Children.Any(c => matches.Contains(c.Key));
                rows.Add(new(item, expanded)); if (expanded) Walk(item.Children);
            }
        }
        Walk(Tree.Roots); _filter = filter; ProjectionBuildCount++; return _visible = rows.AsReadOnly();
    }
}
