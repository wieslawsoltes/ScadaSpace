using ScadaSpace.Core;

namespace ScadaSpace.Runtime;

public sealed partial class ViewInstance
{
    private readonly Dictionary<string, BindingIndex> _bindingIndexes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PropertyPath> _paths = new(StringComparer.Ordinal);
    public long EvaluationCount { get; private set; }
    public long BindingIndexBuildCount { get; private set; }
    public long PropertyPathParseCount { get; private set; }
    public int CachedPropertyPathCount => _paths.Count;

    private sealed class BindingIndex
    {
        private readonly Dictionary<string, BindingDefinition> _exact = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string[]> _descendants;
        private readonly KeyValuePair<string, BindingDefinition>[] _entries;
        private readonly Dictionary<string, BindingDefinition> _source;
        public BindingIndex(ComponentNode node)
        {
            _source = node.Bindings; _entries = node.Bindings.ToArray();
            var descendants = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var (key, binding) in _entries)
            {
                var path = Qualify(key);
                // Preserve first-entry precedence for legacy qualified/unqualified aliases.
                _exact.TryAdd(path, binding);
                for (var i = 1; i < path.Length; i++)
                    if (path[i] is '.' or '[')
                    {
                        var parent = path[..i];
                        if (!descendants.TryGetValue(parent, out var paths)) descendants[parent] = paths = [];
                        paths.Add(path);
                    }
            }
            _descendants = descendants.ToDictionary(p => p.Key, p => p.Value.OrderBy(x => x.Length).ThenBy(x => x, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        }
        public bool Matches(ComponentNode node)
        {
            if (!ReferenceEquals(_source, node.Bindings) || _entries.Length != node.Bindings.Count) return false;
            var index = 0;
            foreach (var entry in node.Bindings)
            {
                var old = _entries[index++];
                if (old.Key != entry.Key || !ReferenceEquals(old.Value, entry.Value)) return false;
            }
            return true;
        }
        public bool SameCollection(ComponentNode node) => ReferenceEquals(_source, node.Bindings) && _entries.Length == node.Bindings.Count;
        public (BindingDefinition? Binding, string Path) Find(string path)
        {
            if (_exact.TryGetValue(path, out var binding)) return (binding, path);
            for (var i = path.Length - 1; i > 0; i--)
                if (path[i] is '.' or '[' && _exact.TryGetValue(path[..i], out binding)) return (binding, path[..i]);
            return (null, "");
        }
        public IReadOnlyList<string> Descendants(string path) => _descendants.TryGetValue(path, out var values) ? values : Array.Empty<string>();
    }
    // Definitions are mutable in the designer. Recheck their shape once per evaluation,
    // not on every recursive read; Source/Enabled/Transforms remain live references.
    private void RefreshBindingIndexes()
    {
        foreach (var node in _nodes.Values)
            if (!_bindingIndexes.TryGetValue(node.Id, out var index) || !index.Matches(node)) Reindex(node);
    }
    private BindingIndex Reindex(ComponentNode node)
    {
        var index = new BindingIndex(node); _bindingIndexes[node.Id] = index; BindingIndexBuildCount++; return index;
    }
    private BindingIndex Index(ComponentNode node) => _bindingIndexes.TryGetValue(node.Id, out var index) && index.SameCollection(node) ? index : Reindex(node);
    private (BindingDefinition? Binding, string Path) FindBinding(ComponentNode node, string path) => Index(node).Find(path);
    private PropertyPath ParsedPath(string path)
    {
        if (_paths.TryGetValue(path, out var parsed)) return parsed;
        parsed = PropertyPath.Parse(path);
        if (_paths.Count >= 512) _paths.Clear();
        _paths[path] = parsed; PropertyPathParseCount++; return parsed;
    }
}
