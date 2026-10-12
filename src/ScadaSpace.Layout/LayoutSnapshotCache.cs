using ScadaSpace.Core;

namespace ScadaSpace.Layout;

/// <summary>Caller-revision-keyed design layout. The caller must advance revision after every definition edit.</summary>
public sealed class LayoutSnapshotCache
{
    private ComponentNode? _root;
    private double _width, _height;
    private long _revision;
    private IReadOnlyList<LayoutSlot> _slots = Array.Empty<LayoutSlot>();
    public long BuildCount { get; private set; }
    public IReadOnlyList<LayoutSlot> Get(ComponentNode root, double width, double height, long revision)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 0 || height < 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (ReferenceEquals(root, _root) && width == _width && height == _height && revision == _revision) return _slots;
        var slots = Array.AsReadOnly(LayoutEngine.Flatten(root, width, height).ToArray());
        _root = root; _width = width; _height = height; _revision = revision; _slots = slots; BuildCount++;
        return slots;
    }
    public void Clear() { _root = null; _slots = Array.Empty<LayoutSlot>(); }
}
