using ScadaSpace.Core;

namespace ScadaSpace.Designer;

public enum Alignment { Left, Right, Top, Bottom, HorizontalCenter, VerticalCenter }
public sealed record EditRecord(string Label, string Before, string After);

/// <summary>Validated, atomic model edits with bounded undo/redo. Mutations must use Execute to participate in change tracking.</summary>
public sealed partial class DesignerDocument
{
    private sealed record HistoryEntry(EditRecord Edit, long BeforeState, long AfterState)
    {
        public long Bytes => 2L * (Edit.Before.Length + Edit.After.Length);
    }
    private readonly List<HistoryEntry> _undo = [];
    private readonly Stack<HistoryEntry> _redo = new();
    private long _sequence;
    private long _state;
    private long _savedState;
    public ProjectDefinition Project { get; private set; }
    public HashSet<string> Selection { get; } = new(StringComparer.Ordinal);
    public long Generation { get; private set; }
    public bool IsDirty => _state != _savedState;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoLabel => _undo.LastOrDefault()?.Edit.Label;
    public long HistoryBytes => _undo.Sum(e => e.Bytes) + _redo.Sum(e => e.Bytes);
    public event Action? Changed;
    public DesignerDocument(ProjectDefinition project) => Project = ProjectCodec.Clone(project);
    public void Execute(string label, Action<ProjectDefinition> edit)
    {
        if (_editing) throw new InvalidOperationException("Document transactions cannot be nested.");
        var before = ProjectCodec.Serialize(Project);
        var selection = Selection.ToArray();
        string after;
        _editing = true; _nodeIndex = null;
        try
        {
            edit(Project);
            var errors = ProjectCodec.Validate(Project);
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(x => x.Message)));
            after = ProjectCodec.Serialize(Project);
            if (before == after) return;
            if (System.Text.Encoding.UTF8.GetByteCount(after) > ProjectCodec.MaximumBytes) throw new InvalidOperationException("Edited project exceeds the 8 MiB limit.");
        }
        catch
        {
            Project = ProjectCodec.Deserialize(before);
            Selection.Clear(); Selection.UnionWith(selection);
            _editing = false; _nodeIndex = null; NotifyRestored();
            throw;
        }
        finally { _editing = false; _nodeIndex = null; }
        var previousState = _state;
        _state = ++_sequence;
        _undo.Add(new(new(label, before, after), previousState, _state));
        _redo.Clear();
        while (_undo.Count > 1 && (_undo.Count > 100 || HistoryBytes > 64L * 1024 * 1024)) _undo.RemoveAt(0);
        Generation++; PruneSelection();
        // Observer errors cannot roll back a committed transaction while leaving its history applied.
        Changed?.Invoke();
    }
    public bool Undo()
    {
        if (_editing) throw new InvalidOperationException("Cannot undo an in-progress transaction.");
        if (_undo.Count == 0) return false;
        var entry = _undo[^1]; var restored = ProjectCodec.Deserialize(entry.Edit.Before);
        _undo.RemoveAt(_undo.Count - 1); _redo.Push(entry); Project = restored; _nodeIndex = null; _state = entry.BeforeState;
        Generation++; PruneSelection(); Changed?.Invoke(); return true;
    }
    public bool Redo()
    {
        if (_editing) throw new InvalidOperationException("Cannot redo an in-progress transaction.");
        if (!_redo.TryPeek(out var entry)) return false;
        var restored = ProjectCodec.Deserialize(entry.Edit.After);
        _redo.Pop(); _undo.Add(entry); Project = restored; _nodeIndex = null; _state = entry.AfterState;
        Generation++; PruneSelection(); Changed?.Invoke(); return true;
    }
    public void MarkSaved() => _savedState = _state;
    public void MarkUnsaved() => _savedState = -1;
    public void Select(string? id, bool additive = false)
    {
        if (!additive) Selection.Clear();
        if (id is not null && Find(id) is not null) { if (additive && Selection.Contains(id)) Selection.Remove(id); else Selection.Add(id); }
    }
    private void PruneSelection() => Selection.RemoveWhere(id => Find(id) is null);
    public void Move(double dx, double dy, double grid = 1)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy) || !double.IsFinite(grid) || grid <= 0) throw new ArgumentException("Invalid movement.");
        Execute("Move components", _ =>
        {
            foreach (var node in SelectedRoots().Where(n => !n.Locked))
            {
                var parent = Project.Views.SelectMany(v => v.Root.DescendantsAndSelf()).FirstOrDefault(p => p.Children.Contains(node));
                if (parent is not null && parent.Layout != LayoutKind.Coordinate) throw new InvalidOperationException("This component's position is managed by its parent layout. Edit layout properties or reorder the component instead.");
                node.X = Snap(node.X + dx, grid); node.Y = Snap(node.Y + dy, grid);
            }
        });
    }
    public void Resize(string id, double width, double height, double grid = 1) => Execute("Resize component", _ =>
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || !double.IsFinite(grid) || grid <= 0) throw new ArgumentException("Invalid size.");
        var node = Find(id) ?? throw new ArgumentException("Unknown component.");
        if (node.Locked) return;
        node.Width = Math.Max(16, Snap(width, grid)); node.Height = Math.Max(16, Snap(height, grid));
    });
    public void Delete() => Execute("Delete selection", _ =>
    {
        foreach (var parent in Project.Views.SelectMany(v => v.Root.DescendantsAndSelf()).ToArray()) parent.Children.RemoveAll(n => Selection.Contains(n.Id) && !n.Locked);
    });
    public void Align(Alignment alignment)
    {
        Execute("Align components", _ =>
        {
            var nodes = SelectedRoots().Where(n => !n.Locked).ToArray();
            if (nodes.Length < 2) return;
            var parents = Project.Views.SelectMany(v => v.Root.DescendantsAndSelf()).Where(p => p.Children.Any(nodes.Contains)).ToArray();
            if (parents.Length != 1 || parents[0].Layout != LayoutKind.Coordinate) throw new InvalidOperationException("Alignment requires siblings in a coordinate container.");
            var left = nodes.Min(n => n.X); var top = nodes.Min(n => n.Y);
            var right = nodes.Max(n => n.X + n.Width); var bottom = nodes.Max(n => n.Y + n.Height);
            foreach (var node in nodes)
                switch (alignment)
                {
                    case Alignment.Left: node.X = left; break; case Alignment.Right: node.X = right - node.Width; break;
                    case Alignment.Top: node.Y = top; break; case Alignment.Bottom: node.Y = bottom - node.Height; break;
                    case Alignment.HorizontalCenter: node.X = (left + right - node.Width) / 2; break;
                    case Alignment.VerticalCenter: node.Y = (top + bottom - node.Height) / 2; break;
                    default: throw new ArgumentException("Unknown alignment.");
                }
        });
    }
    public void BringToFront(bool front) => Execute(front ? "Bring to front" : "Send to back", _ =>
    {
        foreach (var parent in Project.Views.SelectMany(v => v.Root.DescendantsAndSelf()).ToArray())
        {
            var selected = parent.Children.Where(n => Selection.Contains(n.Id) && !n.Locked).ToArray();
            foreach (var node in selected) parent.Children.Remove(node);
            if (front) parent.Children.AddRange(selected); else parent.Children.InsertRange(0, selected);
        }
    });
    public string CopySelection() => System.Text.Json.JsonSerializer.Serialize(SelectedRoots().ToArray(), ProjectCodec.Options);
    public void Paste(string json, string parentId)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > ProjectCodec.MaximumBytes) throw new ArgumentException("Clipboard is too large.");
        var nodes = System.Text.Json.JsonSerializer.Deserialize<ComponentNode[]>(json, ProjectCodec.Options) ?? throw new FormatException("Invalid component clipboard.");
        if (nodes.Any(n => n is null)) throw new FormatException("Clipboard cannot contain null components.");
        var mapping = nodes.SelectMany(n => n.DescendantsAndSelf()).ToDictionary(n => n.Id, _ => Guid.NewGuid().ToString("N"), StringComparer.Ordinal);
        foreach (var node in nodes.SelectMany(n => n.DescendantsAndSelf()))
        {
            node.Id = mapping[node.Id];
            foreach (var binding in node.Bindings.Values)
            {
                foreach (var (oldId, newId) in mapping)
                {
                    binding.Source = binding.Source.Replace(oldId + ".props.", newId + ".props.", StringComparison.Ordinal).Replace(oldId + ".custom.", newId + ".custom.", StringComparison.Ordinal);
                    foreach (var transform in binding.Transforms) transform.Expression = transform.Expression.Replace(oldId + ".props.", newId + ".props.", StringComparison.Ordinal).Replace(oldId + ".custom.", newId + ".custom.", StringComparison.Ordinal);
                }
            }
        }
        Execute("Paste components", _ =>
        {
            var parent = Find(parentId) ?? throw new ArgumentException("Unknown target container.");
            if (parent.Type != "Container") throw new ArgumentException("Paste target must be a container.");
            Selection.Clear();
            foreach (var node in nodes) { node.Name += "Copy"; node.X += 16; node.Y += 16; parent.Children.Add(node); Selection.Add(node.Id); }
        });
    }
    public static double Snap(double value, double grid)
    {
        if (!double.IsFinite(value) || !double.IsFinite(grid) || grid <= 0) throw new ArgumentException("Snap inputs must be finite, with a positive grid size.");
        return Math.Round(value / grid, MidpointRounding.AwayFromZero) * grid;
    }
}
