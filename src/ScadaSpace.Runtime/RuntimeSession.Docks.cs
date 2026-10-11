using ScadaSpace.Core;

namespace ScadaSpace.Runtime;

public sealed record DockSnapshot(Guid InstanceId, DockDefinition Definition, bool IsOpen, bool PushesContent, double Size, IReadOnlyDictionary<string, Scalar> Parameters);

public sealed partial class RuntimeSession
{
    private sealed class DockState(DockDefinition definition)
    {
        public Guid InstanceId { get; } = Guid.NewGuid();
        public DockDefinition Definition { get; } = definition;
        public bool? Open { get; set; }
        public double Size { get; set; } = definition.Size;
        public IReadOnlyDictionary<string, Scalar> Parameters { get; set; } = Snapshot(definition.Parameters);
        public bool IsOpen(double width) => Definition.Display == DockDisplay.Visible || (Open ?? (Definition.Display == DockDisplay.Auto && width >= Definition.AutoBreakpoint));
    }
    private readonly Dictionary<string, DockState> _docks = new(StringComparer.Ordinal);
    public double ViewportWidth { get; private set; } = 1024;
    public CornerPriority CornerPriority => Current?.Page.CornerPriority ?? _runtime.Project.SharedCornerPriority;
    public IReadOnlyList<DockSnapshot> Docks => _docks.Values.Select(d => new DockSnapshot(d.InstanceId, d.Definition, d.IsOpen(ViewportWidth), d.Definition.Content == DockContent.Push || d.Definition.Content == DockContent.Auto && ViewportWidth >= d.Definition.AutoBreakpoint, d.Size, d.Parameters)).ToArray();
    private void ReconcileDocks()
    {
        var local = Current?.Page.Docks ?? [];
        var effective = _runtime.Project.SharedDocks.Where(d => !local.Any(p => p.Edge == d.Edge)).Concat(local).ToDictionary(d => d.Id, StringComparer.Ordinal);
        foreach (var key in _docks.Keys.Where(k => !effective.ContainsKey(k)).ToArray()) _docks.Remove(key);
        foreach (var (id, definition) in effective)
            if (!_docks.TryGetValue(id, out var previous) || !ReferenceEquals(previous.Definition, definition)) _docks[id] = new(definition);
    }
    public void SetViewportWidth(double width)
    {
        if (!double.IsFinite(width) || width < 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (_notifying || width == ViewportWidth) return;
        var changed = false;
        foreach (var dock in _docks.Values)
            if ((ViewportWidth >= dock.Definition.AutoBreakpoint) != (width >= dock.Definition.AutoBreakpoint))
            {
                if (dock.Definition.Display == DockDisplay.Auto) dock.Open = null;
                changed |= dock.Definition.Display == DockDisplay.Auto || dock.Definition.Content == DockContent.Auto;
            }
        ViewportWidth = width; if (changed) Notify();
    }
    public bool OpenDock(string id, IReadOnlyDictionary<string, Scalar>? parameters = null)
    {
        if (_notifying) return false;
        if (!_docks.TryGetValue(id, out var dock)) return Fail("Unknown dock ID: " + id);
        var inputs = new Dictionary<string, Scalar>(dock.Parameters, StringComparer.Ordinal);
        if (parameters is not null) foreach (var pair in parameters) inputs[pair.Key] = pair.Value;
        var frozen = Snapshot(inputs); // Validate before changing any dock state.
        var changed = !dock.IsOpen(ViewportWidth) || !Same(frozen, dock.Parameters);
        if (dock.Definition.Modal)
            foreach (var other in _docks.Values.Where(d => !ReferenceEquals(d, dock) && d.Definition.Modal))
            { changed |= other.IsOpen(ViewportWidth); other.Open = false; }
        dock.Open = true; dock.Parameters = frozen; LastError = null;
        if (changed) Notify(); return true;
    }
    public bool CloseDock(string id)
    {
        if (_notifying) return false;
        if (!_docks.TryGetValue(id, out var dock)) return Fail("Unknown dock ID: " + id);
        if (dock.Definition.Display == DockDisplay.Visible) return Fail("An always-visible dock cannot be closed.");
        var changed = dock.IsOpen(ViewportWidth); dock.Open = false; LastError = null;
        if (changed) Notify(); return true;
    }
    public bool ToggleDock(string id) => _docks.TryGetValue(id, out var dock) ? dock.IsOpen(ViewportWidth) ? CloseDock(id) : OpenDock(id) : Fail("Unknown dock ID: " + id);
    public bool ResizeDock(string id, double size)
    {
        if (_notifying) return false;
        if (!_docks.TryGetValue(id, out var dock)) return Fail("Unknown dock ID: " + id);
        if (!dock.Definition.Resizable || !double.IsFinite(size)) return Fail("Dock is not resizable or size is not finite.");
        size = Math.Clamp(size, dock.Definition.MinimumSize, dock.Definition.MaximumSize);
        if (dock.Size != size) { dock.Size = size; Notify(); }
        LastError = null; return true;
    }
}
