using System.Text.Json;

namespace ScadaSpace.Designer;

/// <summary>Local workspace settings, deliberately separate from portable project content.</summary>
public sealed record DesignerPanels
{
    public int Version { get; init; } = 1;
    public double LeftWidth { get; init; } = 235;
    public double RightWidth { get; init; } = 285;
    public double ProjectFraction { get; init; } = .55;
    public double PaletteFraction { get; init; } = .36;
    public bool ProjectBrowser { get; init; } = true;
    public bool TagBrowser { get; init; } = true;
    public bool PropertyEditor { get; init; } = true;
    public bool Components { get; init; } = true;
    public bool ComponentsPinned { get; init; }
    public bool PaletteTiles { get; init; }
    public bool IsValid => Version == 1 && double.IsFinite(LeftWidth) && LeftWidth is >= 180 and <= 450
        && double.IsFinite(RightWidth) && RightWidth is >= 220 and <= 480
        && double.IsFinite(ProjectFraction) && ProjectFraction is >= .15 and <= .85
        && double.IsFinite(PaletteFraction) && PaletteFraction is >= .15 and <= .85;
    public string Serialize()
    {
        if (!IsValid) throw new InvalidOperationException("Invalid workspace panel configuration.");
        return JsonSerializer.Serialize(this);
    }
    public static DesignerPanels Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        if (json.Length > 4096) throw new FormatException("Workspace settings exceed 4 KiB.");
        try
        {
            var result = JsonSerializer.Deserialize<DesignerPanels>(json, new JsonSerializerOptions { MaxDepth = 4 });
            return result?.IsValid == true ? result : throw new FormatException("Unsupported or invalid workspace settings.");
        }
        catch (JsonException ex) { throw new FormatException("Invalid workspace settings JSON.", ex); }
    }
}

/// <summary>Open resource state. Closing a tab never deletes or reverts project data.</summary>
public sealed class DesignerWorkspace
{
    private readonly HashSet<string> _available = new(StringComparer.Ordinal);
    private readonly List<string> _open = [];
    private IReadOnlyList<string> _snapshot = Array.Empty<string>();
    public IReadOnlyList<string> OpenViews => _snapshot;
    public string? ActiveView { get; private set; }
    public long Revision { get; private set; }
    private void Publish() { _snapshot = Array.AsReadOnly(_open.ToArray()); Revision++; }
    private static string[] Validate(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var array = paths.Take(513).ToArray();
        if (array.Length > 512 || array.Any(p => string.IsNullOrWhiteSpace(p) || p.Length > 1024) || array.Distinct(StringComparer.Ordinal).Count() != array.Length)
            throw new ArgumentException("Expected at most 512 unique view paths.");
        return array;
    }
    public void Reset(IEnumerable<string> paths, string? active = null)
    {
        var array = Validate(paths);
        if (active is not null && !array.Contains(active, StringComparer.Ordinal)) throw new ArgumentException("Unknown active view.");
        _available.Clear(); _available.UnionWith(array); _open.Clear(); _open.AddRange(array);
        ActiveView = active ?? array.FirstOrDefault(); Publish();
    }
    public void Reconcile(IEnumerable<string> paths)
    {
        var array = Validate(paths); var available = array.ToHashSet(StringComparer.Ordinal);
        var removed = _open.RemoveAll(p => !available.Contains(p));
        _available.Clear(); _available.UnionWith(array);
        if (ActiveView is not null && !_available.Contains(ActiveView)) ActiveView = _open.FirstOrDefault();
        if (removed > 0) Publish();
    }
    public bool Open(string path)
    {
        if (!_available.Contains(path)) throw new ArgumentException("Unknown view: " + path);
        if (ActiveView == path) return false;
        if (!_open.Contains(path, StringComparer.Ordinal)) _open.Add(path);
        ActiveView = path; Publish(); return true;
    }
    public bool Close(string path)
    {
        var index = _open.IndexOf(path); if (index < 0) return false;
        _open.RemoveAt(index);
        if (ActiveView == path) ActiveView = _open.Count == 0 ? null : _open[Math.Min(index, _open.Count - 1)];
        Publish(); return true;
    }
    public void CloseOthers(string path)
    {
        if (!_open.Contains(path, StringComparer.Ordinal)) throw new ArgumentException("View is not open.");
        if (_open.Count == 1 && ActiveView == path) return;
        _open.Clear(); _open.Add(path); ActiveView = path; Publish();
    }
    public void CloseAll()
    {
        if (_open.Count == 0) return;
        _open.Clear(); ActiveView = null; Publish();
    }
    public void Cycle(int direction)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (_open.Count < 2) return;
        var index = ActiveView is null ? 0 : _open.IndexOf(ActiveView);
        Open(_open[(index + direction + _open.Count) % _open.Count]);
    }
}
