using ScadaSpace.Core;

namespace ScadaSpace.Designer;

public enum Alignment { Left, Right, Top, Bottom, HorizontalCenter, VerticalCenter }
public sealed record EditRecord(string Label, string Before, string After);

/// <summary>Validated document transactions with bounded undo/redo. An invalid edit restores its entire prior state.</summary>
public sealed class DesignerDocument
{
    private readonly List<EditRecord> _undo = [];
    private readonly Stack<EditRecord> _redo = new();
    private string _saved;
    public ProjectDefinition Project { get; private set; }
    public HashSet<string> Selection { get; } = new(StringComparer.Ordinal);
    public long Generation { get; private set; }
    public bool IsDirty => ProjectCodec.Serialize(Project) != _saved;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoLabel => _undo.LastOrDefault()?.Label;
    public event Action? Changed;
    public DesignerDocument(ProjectDefinition project) { Project = ProjectCodec.Clone(project); _saved = ProjectCodec.Serialize(Project); }
    public void Execute(string label, Action<ProjectDefinition> edit)
    {
        var before = ProjectCodec.Serialize(Project);
        try
        {
            edit(Project);
            var errors = ProjectCodec.Validate(Project);
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(x => x.Message)));
            var after = ProjectCodec.Serialize(Project);
            if (before == after) return;
            _undo.Add(new(label, before, after));
            while (_undo.Count > 100) _undo.RemoveAt(0);
            _redo.Clear(); Generation++; PruneSelection(); Changed?.Invoke();
        }
        catch { Project = ProjectCodec.Deserialize(before); throw; }
    }
    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        var edit = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); _redo.Push(edit);
        Project = ProjectCodec.Deserialize(edit.Before); Generation++; PruneSelection(); Changed?.Invoke(); return true;
    }
    public bool Redo()
    {
        if (!_redo.TryPop(out var edit)) return false;
        _undo.Add(edit); Project = ProjectCodec.Deserialize(edit.After); Generation++; PruneSelection(); Changed?.Invoke(); return true;
    }
    public void MarkSaved() => _saved = ProjectCodec.Serialize(Project);
    public ComponentNode? Find(string id) => Project.Views.SelectMany(v => v.Root.DescendantsAndSelf()).FirstOrDefault(n => n.Id == id);
    public void Select(string? id, bool additive = false)
    {
        if (!additive) Selection.Clear();
        if (id is not null && Find(id) is not null) { if (additive && Selection.Contains(id)) Selection.Remove(id); else Selection.Add(id); }
    }
    private void PruneSelection() => Selection.RemoveWhere(id => Find(id) is null);
    public void Move(double dx, double dy, double grid = 1)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy) || grid <= 0) throw new ArgumentException("Invalid movement.");
        Execute("Move components", _ =>
        {
            foreach (var node in SelectedRoots().Where(n => !n.Locked)) { node.X = Snap(node.X + dx, grid); node.Y = Snap(node.Y + dy, grid); }
        });
    }
    public void Resize(string id, double width, double height, double grid = 1) => Execute("Resize component", _ =>
    {
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
            var left = nodes.Min(n => n.X); var top = nodes.Min(n => n.Y);
            var right = nodes.Max(n => n.X + n.Width); var bottom = nodes.Max(n => n.Y + n.Height);
            foreach (var node in nodes)
                switch (alignment)
                {
                    case Alignment.Left: node.X = left; break; case Alignment.Right: node.X = right - node.Width; break;
                    case Alignment.Top: node.Y = top; break; case Alignment.Bottom: node.Y = bottom - node.Height; break;
                    case Alignment.HorizontalCenter: node.X = (left + right - node.Width) / 2; break;
                    case Alignment.VerticalCenter: node.Y = (top + bottom - node.Height) / 2; break;
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
        if (json.Length > ProjectCodec.MaximumBytes) throw new ArgumentException("Clipboard is too large.");
        var nodes = System.Text.Json.JsonSerializer.Deserialize<ComponentNode[]>(json, ProjectCodec.Options) ?? throw new FormatException("Invalid component clipboard.");
        var mapping = nodes.SelectMany(n => n.DescendantsAndSelf()).ToDictionary(n => n.Id, _ => Guid.NewGuid().ToString("N"), StringComparer.Ordinal);
        foreach (var node in nodes.SelectMany(n => n.DescendantsAndSelf()))
        {
            node.Id = mapping[node.Id];
            foreach (var binding in node.Bindings.Values)
                foreach (var (oldId, newId) in mapping) binding.Source = binding.Source.Replace(oldId + ".props.", newId + ".props.", StringComparison.Ordinal);
        }
        Execute("Paste components", _ =>
        {
            var parent = Find(parentId) ?? throw new ArgumentException("Unknown target container.");
            if (parent.Type != "Container") throw new ArgumentException("Paste target must be a container.");
            Selection.Clear();
            foreach (var node in nodes) { node.Name += "Copy"; node.X += 16; node.Y += 16; parent.Children.Add(node); Selection.Add(node.Id); }
        });
    }
    private IEnumerable<ComponentNode> SelectedRoots()
    {
        var selected = Project.Views.SelectMany(v => v.Root.DescendantsAndSelf()).Where(n => Selection.Contains(n.Id)).ToArray();
        return selected.Where(n => !selected.Any(other => other != n && other.DescendantsAndSelf().Skip(1).Any(child => child.Id == n.Id)));
    }
    public static double Snap(double value, double grid) => Math.Round(value / grid, MidpointRounding.AwayFromZero) * grid;
}
