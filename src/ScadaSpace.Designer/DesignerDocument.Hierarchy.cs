using ScadaSpace.Core;

namespace ScadaSpace.Designer;

public sealed partial class DesignerDocument
{
    private sealed record NodeLocation(ComponentNode Node, ComponentNode? Parent, ViewDefinition View);
    private Dictionary<string, NodeLocation>? _nodeIndex;
    private bool _editing;
    private readonly List<Diagnostic> _restoreDiagnostics = [];
    /// <summary>Raised after rollback replaces project objects. Consumers must rebind even though Generation is unchanged.</summary>
    public event Action? Restored;
    public IReadOnlyList<Diagnostic> RestoreDiagnostics => _restoreDiagnostics.AsReadOnly();
    private void NotifyRestored()
    {
        if (Restored is not { } handlers) return;
        foreach (Action handler in handlers.GetInvocationList())
            try { handler(); }
            catch (Exception error)
            {
                if (_restoreDiagnostics.Count == 32) _restoreDiagnostics.RemoveAt(0);
                _restoreDiagnostics.Add(new("RESTORE_OBSERVER", error.Message));
            }
    }
    public long HierarchyBuildCount { get; private set; }

    // Definitions remain mutable. Execute invalidates before and after each mutation,
    // including rollback; lookups inside a transaction never trust an earlier index.
    private Dictionary<string, NodeLocation> NodeIndex()
    {
        if (!_editing && _nodeIndex is not null) return _nodeIndex;
        var next = new Dictionary<string, NodeLocation>(StringComparer.Ordinal);
        void Add(ComponentNode node, ComponentNode? parent, ViewDefinition view, int depth)
        {
            if (node is null || depth > 24 || next.Count >= 10000 || !next.TryAdd(node.Id, new(node, parent, view)))
                throw new InvalidOperationException("Invalid component hierarchy.");
            foreach (var child in node.Children) Add(child, node, view, depth + 1);
        }
        foreach (var view in Project.Views) Add(view.Root, null, view, 0);
        HierarchyBuildCount++;
        if (!_editing) _nodeIndex = next;
        return next;
    }
    public ComponentNode? Find(string id) => NodeIndex().GetValueOrDefault(id)?.Node;
    public ComponentNode? FindParent(string id) => NodeIndex().GetValueOrDefault(id)?.Parent;
    public ViewDefinition? FindView(string id) => NodeIndex().GetValueOrDefault(id)?.View;

    private IEnumerable<ComponentNode> SelectedRoots()
    {
        var index = NodeIndex();
        foreach (var entry in index.Values)
        {
            if (!Selection.Contains(entry.Node.Id)) continue;
            var ancestor = entry.Parent;
            while (ancestor is not null && !Selection.Contains(ancestor.Id)) ancestor = index[ancestor.Id].Parent;
            if (ancestor is null) yield return entry.Node;
        }
    }
}
